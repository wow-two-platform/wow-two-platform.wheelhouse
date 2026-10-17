namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents the wow2 catalog as the console lists it.</summary>
public sealed record CatalogDto
{
    /// <summary>Gets where the catalog was read from.</summary>
    public required CatalogSourceDto Source { get; init; }

    /// <summary>Gets the declared workspaces, in file order.</summary>
    public required IReadOnlyList<CatalogWorkspaceDto> Workspaces { get; init; }

    /// <summary>Gets the entries, in file order.</summary>
    public required IReadOnlyList<CatalogEntryDto> Entries { get; init; }
}
