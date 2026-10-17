using Wheelhouse.Domain.Operations.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Thirty days of host and container readings, for trends.</summary>
public interface IVitalsHistory
{
    /// <summary>Stores one sampling pass.</summary>
    Task AppendAsync(IReadOnlyList<VitalsSample> samples, CancellationToken ct = default);

    /// <summary>Lists samples since <paramref name="since"/>, oldest first: one target's, or every server's and target's.</summary>
    Task<IReadOnlyList<VitalsSample>> ListAsync(string? targetId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Deletes samples older than <paramref name="before"/>; returns how many went.</summary>
    Task<int> PruneAsync(DateTimeOffset before, CancellationToken ct = default);
}
