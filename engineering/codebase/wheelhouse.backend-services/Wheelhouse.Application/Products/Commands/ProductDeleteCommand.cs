using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Products.Commands;

/// <summary>Represents a command to remove a product that no target runs any more.</summary>
public sealed record ProductDeleteCommand : ICommand<AppResult<Unit>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the slug of the product to remove.</summary>
    public required string Slug { get; init; }

    /// <inheritdoc />
    public string AuditAction => "product.delete";

    /// <inheritdoc />
    public string AuditSubject => Slug;
}
