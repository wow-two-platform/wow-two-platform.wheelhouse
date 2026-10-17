using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Workspaces.Constants;
using Wheelhouse.Domain.Workspaces.Entities;

namespace Wheelhouse.Application.Workspaces.Services;

/// <summary>Provides the workspace a product lands in when nothing names another: <c>own-ventures</c>, added back last
/// in the order when it was removed.</summary>
public sealed class DefaultWorkspaceService(IWorkspacesRepository workspaces)
{
    /// <summary>Gets the default workspace, adding it when it is missing.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The default workspace.</returns>
    public async Task<WorkspaceEntity> GetAsync(CancellationToken ct) =>
        await workspaces.GetBySlugAsync(WorkspaceConstants.DefaultSlug, ct)
        ?? await workspaces.CreateAsync(new WorkspaceEntity
        {
            Id = Guid.CreateVersion7(),
            Slug = WorkspaceConstants.DefaultSlug,
            Name = WorkspaceConstants.DefaultName,
            Order = await workspaces.NextOrderAsync(ct),
        }, ct);
}
