using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Inventory;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Servers.Commands;

/// <summary>Represents a command to remove a server no target or vault runs on any more.</summary>
public sealed record ServerDeleteCommand : ICommand<AppResult<Unit>>, IAuditedCommand, IInventoryCommand
{
    /// <summary>Gets the slug of the server to remove.</summary>
    public required string Slug { get; init; }

    /// <inheritdoc />
    public string AuditAction => "server.delete";

    /// <inheritdoc />
    public string AuditSubject => Slug;
}
