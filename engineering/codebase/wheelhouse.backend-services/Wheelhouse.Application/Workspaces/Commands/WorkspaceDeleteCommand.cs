using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Workspaces.Commands;

/// <summary>Represents a command to remove a workspace that holds no product; pins on it go with it.</summary>
public sealed record WorkspaceDeleteCommand : ICommand<AppResult<Unit>>, IAuditedCommand
{
    /// <summary>Gets the workspace's identifier.</summary>
    public required Guid Id { get; init; }

    /// <inheritdoc />
    public string AuditAction => "workspace.delete";

    /// <inheritdoc />
    public string AuditSubject => Id.ToString();
}
