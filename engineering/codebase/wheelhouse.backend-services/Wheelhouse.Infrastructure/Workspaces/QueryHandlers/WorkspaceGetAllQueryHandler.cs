using Wheelhouse.Infrastructure.Workspaces.Mappers;
using Wheelhouse.Application;
using Wheelhouse.Application.Workspaces;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Workspaces.Models;
using Wheelhouse.Application.Workspaces.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Workspaces.QueryHandlers;

/// <summary>Handles <see cref="WorkspaceGetAllQuery"/>.</summary>
public sealed class WorkspaceGetAllQueryHandler(IWorkspacesRepository workspaces)
    : IQueryHandler<WorkspaceGetAllQuery, AppResult<IReadOnlyList<WorkspaceDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<WorkspaceDto>>> HandleAsync(
        WorkspaceGetAllQuery request, CancellationToken cancellationToken) =>
        AppResult<IReadOnlyList<WorkspaceDto>>.Ok(
            [.. (await workspaces.GetAllAsync(cancellationToken)).Select(WorkspaceMapper.Map)]);
}
