using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Workspaces.Commands;
using Wheelhouse.Application.Workspaces.Mappers;
using Wheelhouse.Application.Workspaces.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Workspaces.CommandHandlers;

/// <summary>Handles <see cref="WorkspaceUpdateCommand"/>.</summary>
public sealed class WorkspaceUpdateCommandHandler(IWorkspacesRepository workspaces)
    : ICommandHandler<WorkspaceUpdateCommand, AppResult<WorkspaceDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<WorkspaceDto>> HandleAsync(WorkspaceUpdateCommand request, CancellationToken cancellationToken)
    {
        var workspace = await workspaces.GetByIdAsync(request.Id, cancellationToken);
        if (workspace is null)
            return AppResult<WorkspaceDto>.Fail(AppErrorFactory.NotFound($"Workspace '{request.Id}' was not found."));

        workspace.Name = request.Name.Trim();
        workspace.Description = WorkspaceText.Description(request.Description);
        await workspaces.UpdateAsync(workspace, cancellationToken);
        return AppResult<WorkspaceDto>.Ok(WorkspaceMapper.Map(workspace));
    }
}
