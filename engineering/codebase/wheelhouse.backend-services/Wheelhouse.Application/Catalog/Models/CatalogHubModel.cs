namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents the hub a catalog file names itself: the repository that owns a set of entries.</summary>
public sealed record CatalogHubModel
{
    /// <summary>Gets the hub's slug.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }
}
