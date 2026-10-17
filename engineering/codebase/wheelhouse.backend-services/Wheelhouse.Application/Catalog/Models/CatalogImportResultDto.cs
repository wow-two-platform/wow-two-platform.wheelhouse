namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents what an import changed, by slug, and anything worth the operator's attention.</summary>
public sealed record CatalogImportResultDto
{
    /// <summary>Gets the slugs of the workspaces the import created.</summary>
    public required IReadOnlyList<string> CreatedWorkspaces { get; init; }

    /// <summary>Gets the slugs of the products the import created.</summary>
    public required IReadOnlyList<string> CreatedProducts { get; init; }

    /// <summary>Gets the slugs of the existing products the import moved into their entry's workspace.</summary>
    public required IReadOnlyList<string> MovedProducts { get; init; }

    /// <summary>Gets one note per entry the import skipped or created without images.</summary>
    public required IReadOnlyList<CatalogImportWarningDto> Warnings { get; init; }
}
