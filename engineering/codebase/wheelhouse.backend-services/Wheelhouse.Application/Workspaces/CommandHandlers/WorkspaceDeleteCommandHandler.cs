using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Workspaces.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Workspaces.CommandHandlers;

/// <summary>Handles <see cref="WorkspaceDeleteCommand"/>; a workspace that still holds a product stays.</summary>
public sealed class WorkspaceDeleteCommandHandler(IWorkspacesRepository workspaces, IProductsRepository products)
    : ICommandHandler<WorkspaceDeleteCommand, AppResult<Unit>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<Unit>> HandleAsync(WorkspaceDeleteCommand request, CancellationToken cancellationToken)
    {
        var workspace = await workspaces.GetByIdAsync(request.Id, cancellationToken);
        if (workspace is null)
            return AppResult<Unit>.Fail(AppErrorFactory.NotFound($"Workspace '{request.Id}' was not found."));
        if (await products.AnyInWorkspaceAsync(workspace.Id, cancellationToken))
            return AppResult<Unit>.Fail(AppErrorFactory.Conflict(
                $"Workspace '{workspace.Slug}' still holds products; move them to another workspace first."));

        await workspaces.DeleteAsync(workspace, cancellationToken);
        return AppResult<Unit>.Ok(default);
    }
}
