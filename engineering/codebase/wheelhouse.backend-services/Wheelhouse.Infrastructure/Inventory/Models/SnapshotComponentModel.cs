namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents one further repository of a product in the runner's inventory (decision 70).</summary>
public sealed record SnapshotComponentModel
{
    /// <summary>Gets the GitHub repository, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the repository's release source, or <c>null</c> while it deploys nothing.</summary>
    public SnapshotReleaseModel? Release { get; init; }
}
