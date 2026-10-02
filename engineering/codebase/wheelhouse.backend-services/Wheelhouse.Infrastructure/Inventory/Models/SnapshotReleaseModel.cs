namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents a product's release source in the runner's inventory.</summary>
public sealed record SnapshotReleaseModel
{
    /// <summary>Gets the asset every published release carries.</summary>
    public required string Asset { get; init; }

    /// <summary>Gets the workflow that builds a commit, or <c>null</c>.</summary>
    public string? Workflow { get; init; }

    /// <summary>Gets each service's image repository.</summary>
    public required IReadOnlyList<SnapshotImageModel> Images { get; init; }
}
