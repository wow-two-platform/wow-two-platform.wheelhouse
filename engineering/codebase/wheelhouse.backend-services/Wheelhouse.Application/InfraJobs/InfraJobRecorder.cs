using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Domain.Servers.Models;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.InfraJobs;

/// <summary>Records every finished infrastructure job the runner holds, once: each as the owner who started it, then
/// marked recorded on the runner. A job that cannot be recorded now is left for the next pass.</summary>
public sealed partial class InfraJobRecorder(
    IInfraGateway gateway, InfraFollowSchedule schedule, IServiceScopeFactory scopes, ILogger<InfraJobRecorder> logger)
{
    /// <summary>Records the finished jobs when the follower is due: at once after the console starts a job, then on
    /// every pass while a job is unfinished or a finished one is left unrecorded, else once per idle interval, which
    /// also catches a job started outside the console.</summary>
    /// <param name="idle">How long the follower waits between passes while nothing is unfinished.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>How many were recorded; zero when the runner was not read.</returns>
    public async Task<int> FollowAsync(TimeSpan idle, CancellationToken ct)
    {
        if (!schedule.TryBegin(out var ticket))
            return 0;
        if (await gateway.FinishedAsync(ct) is not AppResult<JsonElement>.Success { Data: var document })
        {
            schedule.Missed(ticket, idle);
            return 0;
        }
        var (recorded, left) = await RecordAsync(document, ct);
        schedule.Settle(ticket, Open(document) || left > 0, null, idle);
        return recorded;
    }

    /// <summary>Records the finished jobs now.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>How many were recorded.</returns>
    public async Task<int> PassAsync(CancellationToken ct) =>
        await gateway.FinishedAsync(ct) is AppResult<JsonElement>.Success { Data: var document }
            ? (await RecordAsync(document, ct)).Recorded
            : 0;

    /// <summary>Records each finished job the runner listed; returns how many were recorded and how many were left for
    /// the next pass. @internal</summary>
    private async Task<(int Recorded, int Left)> RecordAsync(JsonElement document, CancellationToken ct)
    {
        var jobs = Jobs(document);
        if (jobs.ValueKind != JsonValueKind.Array)
            return (0, 0);

        int recorded = 0, left = 0;
        foreach (var job in jobs.EnumerateArray())
        {
            if (Parse(job) is not { } command)
                continue;
            await using (var scope = scopes.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<BackgroundActor>().Actor = Text(job, "actor") ?? "automation";
                await scope.ServiceProvider.GetRequiredService<ISender>().SendAsync(command, ct);
            }

            if (await gateway.AcknowledgeAsync(command.Id, ct) is AppResult<JsonElement>.Success)
            {
                recorded++;
            }
            else
            {
                left++;
                LogUnacknowledged(logger, command.Kind);
            }
        }

        return (recorded, left);
    }

    /// <summary>The finished jobs: the runner's <c>jobs</c>, beside how many are still unfinished. @internal</summary>
    private static JsonElement Jobs(JsonElement document) =>
        document.ValueKind == JsonValueKind.Object && document.TryGetProperty("jobs", out var jobs) ? jobs : default;

    /// <summary>Whether the runner holds a job still queued or running; an answer without the count reads as one, so
    /// the next pass reads again. @internal</summary>
    private static bool Open(JsonElement document) =>
        document.ValueKind != JsonValueKind.Object || !document.TryGetProperty("open", out var open)
        || open.ValueKind != JsonValueKind.Number || !open.TryGetInt32(out var count) || count > 0;

    private static InfraJobRecordCommand? Parse(JsonElement job)
    {
        var kind = Text(job, "kind");
        if (job.ValueKind != JsonValueKind.Object || Text(job, "id") is not { } id || !Guid.TryParse(id, out _)
            || Text(job, "serverId") is not { } server || Text(job, "status") is not { } status
            || kind is not (InfraJobRecordCommand.Enrollment or InfraJobRecordCommand.Placement
                or InfraJobRecordCommand.Upkeep))
            return null;
        var mints = job.TryGetProperty("mints", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(mint => mint.ValueKind == JsonValueKind.Object).Select(mint =>
                    (Text(mint, "keyId") ?? "?") + " " + string.Join("+", mint.TryGetProperty("tags", out var tags)
                        && tags.ValueKind == JsonValueKind.Array
                            ? tags.EnumerateArray().Select(tag => tag.GetString() ?? "")
                            : []) + (Text(mint, "expires") is { } expires ? " until " + expires : ""))
                .Take(10).ToList()
            : [];
        var facts = job.TryGetProperty("facts", out var found) && found.ValueKind == JsonValueKind.Object ? found : default;
        return new InfraJobRecordCommand
        {
            Kind = kind, Id = id, Server = server, Product = Text(job, "product"), Status = status,
            Reason = Text(job, "reason"), Mints = mints,
            TailnetAddress = facts.ValueKind == JsonValueKind.Object ? Text(facts, "tailnetAddress") : null,
            Ingress = facts.ValueKind == JsonValueKind.Object ? IngressOf(facts) : null,
            PublicAddress = facts.ValueKind == JsonValueKind.Object ? Text(facts, "publicAddress") : null,
            KitDigest = facts.ValueKind == JsonValueKind.Object ? Text(facts, "kitDigest") : null,
        };
    }

    /// <summary>The ingress the kit laid out, as the job recorded it; <c>null</c> when it recorded none or another
    /// shape. The handler validates it before the server takes it.</summary>
    private static ServerIngressValueObject? IngressOf(JsonElement facts)
    {
        if (!facts.TryGetProperty("ingress", out var ingress) || ingress.ValueKind != JsonValueKind.Object)
            return null;
        try
        {
            return ingress.Deserialize<ServerIngressValueObject>(JsonSerializerOptions.Web) is { } read
                ? read with { PublicAddress = null }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "A recorded {Kind} job could not be marked recorded; the next pass records it again")]
    private static partial void LogUnacknowledged(ILogger logger, string kind);
}
