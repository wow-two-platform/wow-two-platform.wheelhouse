using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Caching.Core;
using WoW.Two.Sdk.Beta.Be.Caching.Memo;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Reads the runner's vitals snapshot (<c>transport.py vitals</c>) and holds a successful read in the SDK
/// <see cref="CoalescingMemo{T}"/> for <see cref="DeploymentSettings.VitalsSeconds"/>, so the console's pages, every open
/// tab and the MCP tool share one runner process and one SSH read per host; a fresh read joins a read under way, and a
/// failed read is never held.</summary>
public sealed class RunnerServerVitals(
    IDeploymentGateway gateway, CoalescingMemo<AppResult<JsonElement>> memo, DeploymentSettings settings) : IServerVitals
{
    /// <inheritdoc />
    public async Task<AppResult<JsonElement>> ReadAsync(bool fresh, CancellationToken ct) =>
        await memo.GetAsync(
            async token =>
            {
                var read = await gateway.ReadAsync("vitals", null, token);
                return read is AppResult<JsonElement>.Success { Data: var snapshot }
                    ? CacheResult.Keep(AppResult<JsonElement>.Ok(snapshot.Clone()))
                    : CacheResult.Transient(read);
            },
            TimeSpan.FromSeconds(Math.Clamp(settings.VitalsSeconds, 0, 600)),
            fresh,
            ct);
}
