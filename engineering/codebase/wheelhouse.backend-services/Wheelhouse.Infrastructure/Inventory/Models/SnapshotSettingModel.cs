namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents one service's settings file in the runner's inventory.</summary>
public sealed record SnapshotSettingModel
{
    /// <summary>Gets the service's name.</summary>
    public required string Service { get; init; }

    /// <summary>Gets the file's absolute path on the host.</summary>
    public required string Path { get; init; }
}
