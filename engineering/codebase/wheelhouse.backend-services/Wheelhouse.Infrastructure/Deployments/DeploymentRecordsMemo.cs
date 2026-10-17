using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Holds the last history read for every request scope, so the reads one page makes at once share one runner
/// process; a single gate keeps concurrent misses to one read.</summary>
public sealed class DeploymentRecordsMemo
{
    /// <summary>Gets the gate one history read holds.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Gets or sets the last successful read and when it was taken.</summary>
    public (DateTimeOffset At, IReadOnlyList<DeploymentRecord> Records)? Last { get; set; }
}
