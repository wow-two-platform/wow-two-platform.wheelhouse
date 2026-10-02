using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to change a product's definition.</summary>
public sealed record UpdateProductApiRequest
{
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
