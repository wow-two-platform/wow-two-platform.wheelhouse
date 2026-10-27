using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Reads the runner's vitals snapshot (<c>transport.py vitals</c>) and holds a successful read for
/// <see cref="DeploymentSettings.VitalsSeconds"/>, so the console's pages, every open tab and the MCP tool share one
/// runner process and one SSH read per host; a failed read is never held.</summary>
public sealed class RunnerServerVitals(
    IDeploymentGateway gateway, ServerVitalsMemo memo, DeploymentSettings settings, TimeProvider time) : IServerVitals
{
    /// <inheritdoc />
    public async Task<AppResult<JsonElement>> ReadAsync(bool fresh, CancellationToken ct)
    {
        var freshFor = TimeSpan.FromSeconds(Math.Clamp(settings.VitalsSeconds, 0, 600));
        if (!fresh && Held(freshFor) is { } held)
            return AppResult<JsonElement>.Ok(held);

        var asked = time.GetUtcNow();
        await memo.Gate.WaitAsync(ct);
        try
        {
            // A read that finished while this one waited serves it: a fresh read joins the read under way when it asked.
            if ((fresh ? ReadSince(asked) : Held(freshFor)) is { } shared)
                return AppResult<JsonElement>.Ok(shared);

            var read = await gateway.ReadAsync("vitals", null, ct);
            if (read is AppResult<JsonElement>.Success { Data: var snapshot })
                memo.Last = (time.GetUtcNow(), snapshot.Clone());
            return read;
        }
        finally
        {
            memo.Gate.Release();
        }
    }

    /// <summary>The held snapshot when it was read at or after <paramref name="asked"/>. @internal</summary>
    private JsonElement? ReadSince(DateTimeOffset asked) =>
        memo.Last is { } last && last.At >= asked ? last.Snapshot : null;

    /// <summary>The held snapshot while it is younger than <paramref name="freshFor"/>. @internal</summary>
    private JsonElement? Held(TimeSpan freshFor) =>
        freshFor > TimeSpan.Zero && memo.Last is { } last && time.GetUtcNow() - last.At < freshFor ? last.Snapshot : null;
}
