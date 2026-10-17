using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Workspaces.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Workspaces.Commands;

/// <summary>Represents a command to rename or redescribe a workspace; its slug stays.</summary>
public sealed record WorkspaceUpdateCommand : ICommand<AppResult<WorkspaceDto>>, IAuditedCommand
{
    /// <summary>Gets the workspace's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the workspace holds, or <c>null</c> for none.</summary>
    public string? Description { get; init; }

    /// <inheritdoc />
    public string AuditAction => "workspace.update";

    /// <inheritdoc />
    public string AuditSubject => Id.ToString();

    /// <inheritdoc />
    public string? AuditDetail => Name;
}
