namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents a note about one entry of an import: skipped, or created without images.</summary>
public sealed record CatalogImportWarningDto
{
    /// <summary>Gets the entry's slug.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets what happened, in operator-safe words.</summary>
    public required string Message { get; init; }
}
