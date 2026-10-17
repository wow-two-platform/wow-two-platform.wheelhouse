using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Operations.Entities;

/// <summary>One reading at one instant, kept 30 days for trends: a server's own host figures (no target), or one
/// target's container health. A figure is null where the host hid it, the reading does not carry it, or the server or
/// target could not be read.</summary>
public sealed class VitalsSample : IKeyedEntity<Guid>, IHasTableName
{
    /// <summary>Gets the storage table name — the single source of truth shared by EF mapping and hand-written SQL.</summary>
    public static string TableName => "vitals_samples";

    /// <summary>Gets the sample's unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets or sets the target the sample describes, or <c>null</c> for the server's own host reading.</summary>
    public required string? TargetId { get; set; }

    /// <summary>Gets or sets the server read, or the server the target runs on.</summary>
    public required string ServerId { get; set; }

    /// <summary>Gets or sets the UTC instant of the reading.</summary>
    public DateTimeOffset SampledAtUtc { get; set; }

    /// <summary>Gets or sets whether the server or target could be read at all.</summary>
    public bool Readable { get; set; }

    /// <summary>Gets or sets the one-minute load as a percentage of the host's CPUs.</summary>
    public double? LoadPercent { get; set; }

    /// <summary>Gets or sets the share of the host's memory in use, as a percentage.</summary>
    public double? MemoryPercent { get; set; }

    /// <summary>Gets or sets the fullest observed disk's use, as a percentage.</summary>
    public double? DiskPercent { get; set; }

    /// <summary>Gets or sets how many of the target's containers exist.</summary>
    public int Containers { get; set; }

    /// <summary>Gets or sets how many of them run, and pass their health check where they have one.</summary>
    public int HealthyContainers { get; set; }

    /// <summary>Gets or sets the containers' restart count, summed.</summary>
    public int Restarts { get; set; }
}
