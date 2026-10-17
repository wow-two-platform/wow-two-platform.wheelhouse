using Wheelhouse.Domain.Operations.Entities;

namespace Wheelhouse.Application.Operations;

/// <summary>One stored reading as the dashboard charts it; <c>TargetId</c> is null for a server's own host reading.</summary>
public sealed record VitalsSampleDto(
    string? TargetId, string ServerId, DateTimeOffset SampledAt, bool Readable, double? LoadPercent,
    double? MemoryPercent, double? DiskPercent, int Containers, int HealthyContainers, int Restarts)
{
    /// <summary>Maps a stored sample.</summary>
    public static VitalsSampleDto From(VitalsSample sample) => new(sample.TargetId, sample.ServerId, sample.SampledAtUtc,
        sample.Readable, sample.LoadPercent, sample.MemoryPercent, sample.DiskPercent, sample.Containers,
        sample.HealthyContainers, sample.Restarts);
}
