using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Domain.Products.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.Commands;

/// <summary>Represents a command to change a product's definition; its slug stays.</summary>
public sealed record ProductUpdateCommand : ICommand<AppResult<ProductDto>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the slug of the product to change.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the product does.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the GitHub repository that defines the product, as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the branch releases come from.</summary>
    public required string DefaultBranch { get; init; }

    /// <summary>Gets whether the primary repository publishes releases, or <c>null</c> for none: Wheelhouse derives its
    /// release source from the repository, and a value sent must be the derived one.</summary>
    public ProductReleaseRequest? Release { get; init; }

    /// <summary>Gets the product's further repositories after its primary one, each with its own release source
    /// (decision 70); <c>null</c> to keep them as they are.</summary>
    public IReadOnlyList<ProductComponentValueObject>? Components { get; init; }

    /// <summary>Gets what deploys the product, or <c>null</c> to keep it as it is.</summary>
    public ProductDeployMode? Deploy { get; init; }

    /// <inheritdoc />
    public string AuditAction => "product.update";

    /// <inheritdoc />
    public string AuditSubject => Slug;

    /// <inheritdoc />
    public string? AuditDetail => Repository;
}
