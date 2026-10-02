using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Models;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Products.Commands;

/// <summary>Represents a command to add a product to the portfolio.</summary>
public sealed record ProductCreateCommand : ICommand<AppResult<ProductDto>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the name targets, bundles and URLs will know the product by; fixed once created.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the product does.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the GitHub repository that defines the product, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the branch releases come from.</summary>
    public required string DefaultBranch { get; init; }

    /// <summary>Gets where published releases and commit builds come from, or <c>null</c> for none.</summary>
    public ProductReleaseValueObject? Release { get; init; }

    /// <inheritdoc />
    public string AuditAction => "product.create";

    /// <inheritdoc />
    public string AuditSubject => Slug;

    /// <inheritdoc />
    public string? AuditDetail => Repository;
}
