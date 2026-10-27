using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents where a product's name, description and homepage come from, and what the product's repository
/// says about its services.</summary>
public sealed record ProductIdentityDto
{
    /// <summary>Gets where the name, description and homepage come from; a <see cref="ProductIdentitySource.Manifest"/>
    /// source is edited in the repository, not in the console.</summary>
    public required ProductIdentitySource Source { get; init; }

    /// <summary>Gets the full SHA of the commit the identity was read from, or <c>null</c> when the product's own row is
    /// the source.</summary>
    public string? Commit { get; init; }

    /// <summary>Gets the repository file the identity was read from — <c>engineering/deployment/deploy.yml</c>, or the
    /// retired <c>product/product.json</c> — or <c>null</c> when the product's own row is the source.</summary>
    public string? File { get; init; }

    /// <summary>Gets why the repository's newest identity is ignored, as a sentence that names the file, or <c>null</c>
    /// when nothing is. The previous valid identity, if any, still applies.</summary>
    public string? Problem { get; init; }

    /// <summary>Gets what the owner should change in the repository, such as moving a retired
    /// <c>product/product.json</c> into <c>deploy.yml</c>, or <c>null</c> when nothing.</summary>
    public string? Notice { get; init; }

    /// <summary>Gets the name and description of each service that has its own.</summary>
    public required IReadOnlyList<ProductServiceDto> Services { get; init; }
}
