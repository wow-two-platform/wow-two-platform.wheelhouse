using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Vaults.Commands;

/// <summary>Represents a command to stop administering a vault; the vault and its secrets stay where they run.</summary>
public sealed record VaultDeleteCommand : ICommand<AppResult<Unit>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the slug of the vault to remove.</summary>
    public required string Slug { get; init; }

    /// <inheritdoc />
    public string AuditAction => "vault.delete";

    /// <inheritdoc />
    public string AuditSubject => Slug;
}
