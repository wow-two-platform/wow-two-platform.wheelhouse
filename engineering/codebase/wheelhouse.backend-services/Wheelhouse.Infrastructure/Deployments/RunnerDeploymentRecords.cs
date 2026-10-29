using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Caching.Core;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Reads the runner's deployment history projection and one submission's observation from its local files.
/// One history read serves every request for <c>Deployment:HistorySeconds</c> through the SDK cache, so a page's
/// concurrent reads share one runner process; a record the runner wrote malformed is skipped, never fatal; a failed read
/// is never held.</summary>
public sealed partial class RunnerDeploymentRecords(
    IDeploymentGateway gateway,
    ICacheRepository cache,
    DeploymentSettings settings,
    ILogger<RunnerDeploymentRecords> logger) : IDeploymentRecords
{
    /// <summary>Holds the cache key of the last history read.</summary>
    public const string CacheKey = "deployment-history";

    /// <inheritdoc />
    public async Task<AppResult<IReadOnlyList<DeploymentRecord>>> ListAsync(CancellationToken ct)
    {
        var freshFor = TimeSpan.FromSeconds(Math.Clamp(settings.HistorySeconds, 0, 300));
        if (freshFor == TimeSpan.Zero)
            return await ReadRunnerAsync(ct);

        var entry = new CacheEntryOptions { Expiration = freshFor, LocalCacheExpiration = freshFor };
        var held = await cache.GetOrCreateUnlessTransientAsync<DeploymentRecordsRead>(
            CacheKey,
            async token => await ReadRunnerAsync(token) switch
            {
                AppResult<IReadOnlyList<DeploymentRecord>>.Success { Data: var records } => CacheResult.Keep(new DeploymentRecordsRead(records, null)),
                AppResult<IReadOnlyList<DeploymentRecord>>.Failure { Error: var error } => CacheResult.Transient(new DeploymentRecordsRead([], error)),
                _ => throw new InvalidOperationException("A result is a success or a failure."),
            },
            entry,
            ct);
        return held.Failure is { } failure
            ? AppResult<IReadOnlyList<DeploymentRecord>>.Fail(failure)
            : AppResult<IReadOnlyList<DeploymentRecord>>.Ok(held.Records);
    }

    /// <inheritdoc />
    public async Task<AppResult<DeploymentObservation>> ObserveAsync(Guid id, CancellationToken ct)
    {
        var read = await gateway.ReadAsync("observation", id.ToString(), ct);
        if (read is not AppResult<JsonElement>.Success { Data: var document })
            return AppResult<DeploymentObservation>.Fail(((AppResult<JsonElement>.Failure)read).Error);
        var observation = new DeploymentObservation
        {
            Steps = Each(document, "steps", ParseStep),
            Sites = Each(document, "sites", ParseSite),
            Warnings = Each(document, "warnings", item => item.ValueKind == JsonValueKind.String ? item.GetString() : null),
            ReconciledBy = Text(document, "reconciledBy"),
            Snapshot = ParseSnapshot(document),
            ServiceRollout = ParseServiceRollout(document),
        };
        return AppResult<DeploymentObservation>.Ok(observation);
    }

    /// <summary>Reads the history from the runner, skipping malformed records. @internal</summary>
    private async Task<AppResult<IReadOnlyList<DeploymentRecord>>> ReadRunnerAsync(CancellationToken ct)
    {
        var read = await gateway.ReadAsync("history", null, ct);
        if (read is not AppResult<JsonElement>.Success { Data: var document })
            return AppResult<IReadOnlyList<DeploymentRecord>>.Fail(((AppResult<JsonElement>.Failure)read).Error);
        if (document.ValueKind != JsonValueKind.Array)
            return AppResult<IReadOnlyList<DeploymentRecord>>.Fail(AppErrorFactory.ExternalUnavailable("Deployment history is unreadable."));

        var records = new List<DeploymentRecord>(document.GetArrayLength());
        var skipped = 0;
        foreach (var element in document.EnumerateArray())
        {
            if (Parse(element) is { } record)
                records.Add(record);
            else
                skipped++;
        }

        if (skipped > 0)
            LogSkipped(logger, skipped);
        return AppResult<IReadOnlyList<DeploymentRecord>>.Ok(records);
    }

    /// <summary>Reads one record, or null when it lacks an identity, a status or a submission instant. @internal</summary>
    private static DeploymentRecord? Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        try
        {
            var record = element.Deserialize<DeploymentRecord>(JsonSerializerOptions.Web);
            if (record is not { Id.Length: > 0, Status.Length: > 0 })
                return null;
            // A watch window another rollout ended was recorded `superseded` before the rename to `replaced`.
            var watch = record.Watch is { Status: "superseded" } legacy ? legacy with { Status = "replaced" } : record.Watch;
            return record with { Watch = watch, ServiceRollout = ParseServiceRollout(element) };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Reads each well-formed item of an array property; null when it is absent. @internal</summary>
    private static IReadOnlyList<T>? Each<T>(JsonElement document, string property, Func<JsonElement, T?> read) where T : class =>
        document.ValueKind == JsonValueKind.Object && document.TryGetProperty(property, out var items)
            && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray().Select(read).OfType<T>()]
            : null;

    /// <summary>Reads one step; null without a name and a status. @internal</summary>
    private static DeploymentStepDto? ParseStep(JsonElement step) =>
        Text(step, "name") is { } name && Text(step, "status") is { } status
            ? new DeploymentStepDto(name, status, Instant(step, "startedAt"), Instant(step, "completedAt"), Text(step, "detail"))
            : null;

    /// <summary>Reads one site; null without a name and an http(s) address. @internal</summary>
    private static DeploymentSiteDto? ParseSite(JsonElement site)
    {
        if (Text(site, "name") is not { } name || Text(site, "url") is not { } url
            || !(url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("http://", StringComparison.Ordinal)))
            return null;
        DeploymentSiteProbeDto? probe = null;
        if (site.TryGetProperty("probe", out var reading) && reading.ValueKind == JsonValueKind.Object
            && reading.TryGetProperty("ok", out var ok) && ok.ValueKind is JsonValueKind.True or JsonValueKind.False)
            probe = new DeploymentSiteProbeDto(ok.GetBoolean(),
                reading.TryGetProperty("status", out var status) && status.TryGetInt32(out var code) ? code : null,
                Text(reading, "detail"));
        return new DeploymentSiteDto(name, Text(site, "service"), Text(site, "exposure"), url, probe);
    }

    /// <summary>Reads the snapshot a prod rollout took; null without one or when it is malformed. @internal</summary>
    private static DeploymentSnapshotDto? ParseSnapshot(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object || !document.TryGetProperty("snapshot", out var snapshot)
            || snapshot.ValueKind != JsonValueKind.Object || Text(snapshot, "path") is not { } path)
            return null;
        try
        {
            return new DeploymentSnapshotDto(path, Instant(snapshot, "createdAt"),
                Each(snapshot, "databases", item => item.Deserialize<SnapshotDatabaseDto>(JsonSerializerOptions.Web)) ?? [],
                Each(snapshot, "volumes", item => item.Deserialize<SnapshotVolumeDto>(JsonSerializerOptions.Web)) ?? [],
                Each(snapshot, "restore", item => item.ValueKind == JsonValueKind.String ? item.GetString() : null) ?? []);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads how a rollout treated each service; null when absent or malformed. @internal</summary>
    private static ServiceRolloutDto? ParseServiceRollout(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object || !document.TryGetProperty("serviceRollout", out var rollout)
            || rollout.ValueKind != JsonValueKind.Object)
            return null;
        try
        {
            var services = Each(rollout, "services", item =>
                Text(item, "name") is { } name && Text(item, "change") is { } change && Text(item, "outcome") is { } outcome
                    ? new ServiceRolloutItemDto(name, change, outcome,
                        Each(item, "dependsOn", other => other.ValueKind == JsonValueKind.String ? other.GetString() : null) ?? [],
                        Release(item, "from"), Release(item, "to"), Text(item, "reason"), Text(item, "health"), Instant(item, "at"))
                    : null) ?? [];
            var waves = Each(rollout, "waves", wave => wave.ValueKind == JsonValueKind.Array
                ? (IReadOnlyList<string>)[.. wave.EnumerateArray().Where(name => name.ValueKind == JsonValueKind.String).Select(name => name.GetString()!)]
                : null) ?? [];
            var removed = Each(rollout, "removed", name => name.ValueKind == JsonValueKind.String ? name.GetString() : null) ?? [];
            return new ServiceRolloutDto(
                rollout.TryGetProperty("staged", out var staged) && staged.ValueKind == JsonValueKind.True, waves, removed, services);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A service's release in a rollout: <c>{ version, digest }</c> read as its version, else its 12-hex image
    /// digest; a plain string as itself; null when absent. @internal</summary>
    private static string? Release(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Object => Text(value, "version") ?? Text(value, "digest"),
                _ => null,
            }
            : null;

    /// <summary>A string property, or null. @internal</summary>
    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>An instant property, or null when absent or malformed. @internal</summary>
    private static DateTimeOffset? Instant(JsonElement element, string property) =>
        Text(element, property) is { } text && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var instant) ? instant : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {Count} malformed deployment records in the runner's history.")]
    private static partial void LogSkipped(ILogger logger, int count);
}
