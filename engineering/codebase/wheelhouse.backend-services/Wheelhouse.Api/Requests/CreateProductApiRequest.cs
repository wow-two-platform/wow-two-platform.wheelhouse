using Wheelhouse.Domain.Products.Enums;
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

    /// <summary>Gets the product's further repositories after its primary one, each with its own release source
    /// (decision 70); empty for a product of one repository.</summary>
    public IReadOnlyList<ProductComponentValueObject>? Components { get; init; }

    /// <summary>Gets what deploys the product — <c>wheelhouse</c>, <c>ci</c> or <c>none</c> — or <c>null</c> to start with <c>wheelhouse</c>.</summary>
    public ProductDeployMode? Deploy { get; init; }

    /// <summary>Gets the workspace the product joins, or <c>null</c> for the default workspace, <c>own-ventures</c>.</summary>
    public Guid? WorkspaceId { get; init; }
}
