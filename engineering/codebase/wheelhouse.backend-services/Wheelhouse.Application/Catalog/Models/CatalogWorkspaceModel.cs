namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents a workspace the catalog declares.</summary>
public sealed record CatalogWorkspaceModel
{
    /// <summary>Gets the workspace's slug.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the workspace holds, or <c>null</c> for none.</summary>
    public string? Description { get; init; }
}
