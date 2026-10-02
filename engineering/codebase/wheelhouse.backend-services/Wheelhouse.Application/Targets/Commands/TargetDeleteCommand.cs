using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Targets.Commands;

/// <summary>Represents a command to remove a target from the inventory; what runs on its host stays until the operator
/// tears it down there.</summary>
public sealed record TargetDeleteCommand : ICommand<AppResult<Unit>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the slug of the target to remove.</summary>
    public required string Slug { get; init; }

    /// <inheritdoc />
    public string AuditAction => "target.delete";

    /// <inheritdoc />
    public string AuditSubject => Slug;
}
