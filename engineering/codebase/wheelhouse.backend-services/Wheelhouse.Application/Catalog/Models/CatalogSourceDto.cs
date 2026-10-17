namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents where a catalog read came from.</summary>
public sealed record CatalogSourceDto
{
    /// <summary>Gets the repository, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the catalog's path in the repository.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the ref the catalog was read at.</summary>
    public required string Ref { get; init; }

    /// <summary>Gets the full SHA of the commit the catalog was read from.</summary>
    public required string Commit { get; init; }
}
