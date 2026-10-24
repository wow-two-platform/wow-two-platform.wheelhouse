namespace Wheelhouse.Application.Catalog.Models;

/// <summary>Represents what an import changed, by slug, and anything worth the owner's attention.</summary>
public sealed record CatalogImportResultDto
{
    /// <summary>Gets the slugs of the workspaces the import created.</summary>
    public required IReadOnlyList<string> CreatedWorkspaces { get; init; }

    /// <summary>Gets the slugs of the products the import created.</summary>
    public required IReadOnlyList<string> CreatedProducts { get; init; }

    /// <summary>Gets the slugs of the existing products the import moved into their entry's workspace.</summary>
    public required IReadOnlyList<string> MovedProducts { get; init; }

    /// <summary>Gets the slugs of the existing products the import brought in step with their entry: the deploy mode its
    /// <c>deploy</c> names, its repositories, or a release source for a repository that had none.</summary>
    public IReadOnlyList<string> UpdatedProducts { get; init; } = [];

    /// <summary>Gets the slugs of the products the import retired because an imported entry replaces them: nothing had
    /// deployed to them, so their environments and then the products went.</summary>
    public IReadOnlyList<string> RetiredProducts { get; init; } = [];

    /// <summary>Gets one note per entry the import skipped or created without images.</summary>
    public required IReadOnlyList<CatalogImportWarningDto> Warnings { get; init; }
}
