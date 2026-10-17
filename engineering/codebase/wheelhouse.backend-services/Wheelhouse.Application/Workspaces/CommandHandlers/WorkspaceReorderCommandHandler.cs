using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Workspaces.Commands;
using Wheelhouse.Application.Workspaces.Mappers;
using Wheelhouse.Application.Workspaces.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Workspaces.CommandHandlers;

/// <summary>Handles <see cref="WorkspaceReorderCommand"/>: the list must name every workspace exactly once, so an order
/// read before a workspace came or went is refused rather than half-applied.</summary>
public sealed class WorkspaceReorderCommandHandler(IWorkspacesRepository workspaces)
    : ICommandHandler<WorkspaceReorderCommand, AppResult<IReadOnlyList<WorkspaceDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<WorkspaceDto>>> HandleAsync(
        WorkspaceReorderCommand request, CancellationToken cancellationToken)
    {
        var stored = await workspaces.GetAllAsync(cancellationToken);
        if (!stored.Select(workspace => workspace.Id).ToHashSet().SetEquals(request.Ids))
            return AppResult<IReadOnlyList<WorkspaceDto>>.Fail(AppErrorFactory.Conflict(
                "The order must name every workspace exactly once; reload the workspaces and try again."));

        await workspaces.ReorderAsync(request.Ids, cancellationToken);
        return AppResult<IReadOnlyList<WorkspaceDto>>.Ok(
            [.. (await workspaces.GetAllAsync(cancellationToken)).Select(WorkspaceMapper.Map)]);
    }
}
