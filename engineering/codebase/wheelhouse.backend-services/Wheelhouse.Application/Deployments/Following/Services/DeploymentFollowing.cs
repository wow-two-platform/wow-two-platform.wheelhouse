using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Following.Services;

/// <summary>Observes durable submissions independently of the browser: one bounded runner pass (<c>follow</c>) reads the
/// target journals of unfinished submissions on the runner's own per-job schedule, and never starts, retries or
/// reconciles a deployment. The runner is read only while a submission is unfinished, at the time it names for its next
/// look, and otherwise once per idle interval, which also catches a rollout started outside the console
/// (<c>self-deploy.py</c>, CI); <see cref="DeploymentFollowSchedule"/> keeps that timing.</summary>
public sealed partial class DeploymentFollowing(
    IDeploymentGateway gateway, DeploymentFollowSchedule schedule, ILogger<DeploymentFollowing> logger)
{
    /// <summary>Runs one observation pass when one is due.</summary>
    /// <param name="idle">How long the follower waits between passes while nothing is unfinished.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Whether the runner was read.</returns>
    public async Task<bool> PassAsync(TimeSpan idle, CancellationToken ct)
    {
        if (!schedule.TryBegin(out var ticket))
            return false;
        if (await gateway.ReadAsync("follow", null, ct) is AppResult<JsonElement>.Success { Data: var pass })
        {
            schedule.Settle(ticket, Open(pass), NextIn(pass), idle);
        }
        else
        {
            schedule.Missed(ticket, idle);
            LogObservationsStale(logger);
        }
        return true;
    }

    /// <summary>Whether the pass left submissions unfinished; a pass that another one held names no count, so it counts
    /// as unfinished and the next pass reads again. @internal</summary>
    private static bool Open(JsonElement pass) =>
        pass.ValueKind != JsonValueKind.Object || !pass.TryGetProperty("open", out var open)
        || open.ValueKind != JsonValueKind.Number || !open.TryGetInt32(out var count) || count > 0;

    /// <summary>In how long the runner's earliest next look is due, if it said. @internal</summary>
    private static TimeSpan? NextIn(JsonElement pass) =>
        pass.ValueKind == JsonValueKind.Object && pass.TryGetProperty("nextIn", out var next)
        && next.ValueKind == JsonValueKind.Number && next.TryGetDouble(out var seconds) && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, 0, TimeSpan.FromDays(1).TotalSeconds))
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deployment observations could not be refreshed; the next pass will retry")]
    private static partial void LogObservationsStale(ILogger logger);
}
