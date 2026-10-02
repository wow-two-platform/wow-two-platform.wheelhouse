using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to add a product.</summary>
public sealed record CreateProductApiRequest
{
    /// <summary>Gets the name targets, bundles and URLs will know the product by.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the product does.</summary>
    public string Description { get; init; } = "";

    /// <summary>Gets the GitHub repository, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the branch releases come from.</summary>
    public string DefaultBranch { get; init; } = "main";

    /// <summary>Gets where published releases and commit builds come from, or <c>null</c> for none.</summary>
    public ProductReleaseValueObject? Release { get; init; }
}
