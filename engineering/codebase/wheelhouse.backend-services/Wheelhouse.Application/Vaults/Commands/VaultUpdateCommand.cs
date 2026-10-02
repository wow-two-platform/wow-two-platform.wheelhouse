using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using Wheelhouse.Application.Vaults.Models;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Vaults.Commands;

/// <summary>Represents a command to change a vault's definition; its slug stays.</summary>
public sealed record VaultUpdateCommand : ICommand<AppResult<VaultDto>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the slug of the vault to change.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the slug of the server the vault runs on.</summary>
    public required string Server { get; init; }

    /// <summary>Gets the private management endpoint Wheelhouse reaches.</summary>
    public required string Url { get; init; }

    /// <inheritdoc />
    public string AuditAction => "vault.update";

    /// <inheritdoc />
    public string AuditSubject => Slug;

    /// <inheritdoc />
    public string? AuditDetail => Url + " on " + Server;
}
