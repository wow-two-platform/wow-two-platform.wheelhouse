using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.InfraJobs;

/// <summary>Records every finished infrastructure job the runner holds, once: each as the owner who started it, then
/// marked recorded on the runner. A job that cannot be recorded now is left for the next pass.</summary>
public sealed partial class InfraJobRecorder(
    IInfraGateway gateway, IServiceScopeFactory scopes, ILogger<InfraJobRecorder> logger)
{
    /// <summary>Records the finished jobs.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>How many were recorded.</returns>
    public async Task<int> PassAsync(CancellationToken ct)
    {
        if (await gateway.FinishedAsync(ct) is not AppResult<JsonElement>.Success { Data: var jobs }
            || jobs.ValueKind != JsonValueKind.Array)
            return 0;

        var recorded = 0;
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
                recorded++;
            else
                LogUnacknowledged(logger, command.Kind);
        }

        return recorded;
    }

    private static InfraJobRecordCommand? Parse(JsonElement job)
    {
        var kind = Text(job, "kind");
        if (job.ValueKind != JsonValueKind.Object || Text(job, "id") is not { } id || !Guid.TryParse(id, out _)
            || Text(job, "serverId") is not { } server || Text(job, "status") is not { } status
            || kind is not (InfraJobRecordCommand.Enrollment or InfraJobRecordCommand.Placement))
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
        };
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "A recorded {Kind} job could not be marked recorded; the next pass records it again")]
    private static partial void LogUnacknowledged(ILogger logger, string kind);
}
