using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents a product as integrations read it: identity, lifecycle, release source and where each
/// environment is reached — no targets, servers or deployment state. The identity is the cached manifest of the
/// product's repository when it has a valid one. It is the one <c>catalog:read</c> projection: <c>/api/products</c> and
/// the MCP <c>list_projects</c> tool both return it.</summary>
public sealed record ProductDto
{
    /// <summary>Gets the product's identifier; pins and catalog entries refer to it.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the name targets, bundles, vault namespaces and URLs know the product by.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name, from the product's manifest when it has a valid one, else from its own row.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the product does, from the same source as the name.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the product's public address, or <c>null</c> when its manifest gives none or it has no manifest.</summary>
    public string? Homepage { get; init; }

    /// <summary>Gets where the name, description and homepage come from, and what the repository says about the
    /// product's services.</summary>
    public required ProductIdentityDto Identity { get; init; }

    /// <summary>Gets the workspace the product belongs to.</summary>
    public required Guid WorkspaceId { get; init; }

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
