namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents a smoke check in the runner's inventory.</summary>
public sealed record SnapshotSmokeModel
{
    /// <summary>Gets the service's name.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the path requested.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the status the service must answer.</summary>
    public required int Status { get; init; }
}
