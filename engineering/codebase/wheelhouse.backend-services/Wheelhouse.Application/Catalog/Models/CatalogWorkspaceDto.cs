namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents a workspace the catalog declares, as the console lists it.</summary>
public sealed record CatalogWorkspaceDto
{
    /// <summary>Gets the workspace's slug.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the workspace holds, or <c>null</c> for none.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the repository of the hub that declares the workspace.</summary>
    public required string Source { get; init; }
}
