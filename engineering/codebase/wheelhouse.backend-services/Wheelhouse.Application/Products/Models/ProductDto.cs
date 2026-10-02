using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents a product as integrations read it: identity, lifecycle, release source and where each
/// environment is reached — no targets, servers or deployment state.</summary>
public sealed record ProductDto
{
    /// <summary>Gets the product's identifier.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the product does.</summary>
    public required string Description { get; init; }

    /// <summary>Gets where the product stands in the portfolio.</summary>
    public required ProductLifecycle Lifecycle { get; init; }

    /// <summary>Gets the product's source repository.</summary>
    public required ProductRepositoryDto Repository { get; init; }

    /// <summary>Gets where published releases and commit builds come from, or <c>null</c> when only hand-imported
    /// bundles deploy.</summary>
    public ProductReleaseDto? Release { get; init; }

    /// <summary>Gets the path of the product's icon; it answers not found when the repository carries none.</summary>
    public required string IconUrl { get; init; }

    /// <summary>Gets the product's environments, in dev, test, prod order.</summary>
    public required IReadOnlyList<ProductEnvironmentDto> Environments { get; init; }
}
