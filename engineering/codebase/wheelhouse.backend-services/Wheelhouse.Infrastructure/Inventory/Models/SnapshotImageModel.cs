namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents one service's image repository in the runner's inventory.</summary>
public sealed record SnapshotImageModel
{
    /// <summary>Gets the service's name.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the image repository.</summary>
    public required string Image { get; init; }
}
