using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Operations.Entities;

namespace Wheelhouse.Application.Operations;

/// <summary>One sampling pass: reads every server's host and every target's containers through the runner, stores them
/// and drops samples older than the retention window.</summary>
public sealed class VitalsSampling(IDeploymentGateway gateway, IVitalsHistory history, TimeProvider time)
{
    /// <summary>How long samples are kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>Samples every server and target once; returns how many samples were stored, or null when the runner
    /// failed.</summary>
    public async Task<int?> SampleOnceAsync(CancellationToken ct)
    {
        var vitals = await gateway.ReadAsync("vitals", null, ct);
        if (!vitals.IsSuccess)
            return null;
        var now = time.GetUtcNow();
        var samples = vitals.Match<IReadOnlyList<VitalsSample>>(ok => VitalsSampleReader.Read(ok.Data, now), _ => []);
        await history.AppendAsync(samples, ct);
        await history.PruneAsync(now - Retention, ct);
        return samples.Count;
    }
}
