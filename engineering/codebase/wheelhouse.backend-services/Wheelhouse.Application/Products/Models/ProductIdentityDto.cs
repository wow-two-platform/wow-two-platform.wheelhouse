using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents where a product's name, description and homepage come from, and what the product's repository
/// says about its services.</summary>
public sealed record ProductIdentityDto
{
    /// <summary>Gets where the name, description and homepage come from; a <see cref="ProductIdentitySource.Manifest"/>
    /// source is edited in the repository, not in the console.</summary>
    public required ProductIdentitySource Source { get; init; }

    /// <summary>Gets the full SHA of the commit the manifest was read from, or <c>null</c> when the product's own row is
    /// the source.</summary>
    public string? Commit { get; init; }

    /// <summary>Gets why the repository's newest manifest is ignored, or <c>null</c> when nothing is. The previous valid
    /// manifest, if any, still applies.</summary>
    public string? Problem { get; init; }

    /// <summary>Gets the name and description of each service that has its own.</summary>
    public required IReadOnlyList<ProductServiceDto> Services { get; init; }
}
