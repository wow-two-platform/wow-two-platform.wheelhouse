namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents a product in the runner's inventory.</summary>
public sealed record SnapshotProductModel
{
    /// <summary>Gets the product's slug.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the product does.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the GitHub repository, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the branch releases come from.</summary>
    public required string DefaultBranch { get; init; }

    /// <summary>Gets the release source, or <c>null</c> when only hand-imported bundles deploy.</summary>
    public SnapshotReleaseModel? Release { get; init; }
}
