namespace Wheelhouse.Domain.Products.Models;

/// <summary>Represents one further repository of a product built from several (decision 70): its own CI, version tags
/// and release source. Its <c>deploy.yml</c> names the product's slug, and its services are its own across the product.</summary>
public sealed record ProductComponentValueObject
{
    /// <summary>Gets the GitHub repository, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets where the repository's releases and commit builds come from, or <c>null</c> while it deploys
    /// nothing.</summary>
    public ProductReleaseValueObject? Release { get; init; }
}
