using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Workspaces.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Workspaces.Commands;

/// <summary>Represents a command to put the workspaces in a new order; it names every workspace exactly once.</summary>
public sealed record WorkspaceReorderCommand : ICommand<AppResult<IReadOnlyList<WorkspaceDto>>>, IAuditedCommand
{
    /// <summary>Gets every workspace's identifier, first to last.</summary>
    public required IReadOnlyList<Guid> Ids { get; init; }

    /// <inheritdoc />
    public string AuditAction => "workspace.reorder";

    /// <inheritdoc />
    public string AuditSubject => "workspaces";
}
