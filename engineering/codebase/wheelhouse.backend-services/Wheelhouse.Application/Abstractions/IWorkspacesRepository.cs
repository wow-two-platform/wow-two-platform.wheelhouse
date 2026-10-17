using Wheelhouse.Domain.Workspaces.Entities;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of the workspaces products are grouped into. <see cref="IRepository{TEntity,TId}.GetAllAsync"/>
/// lists them in the operator's order.</summary>
public interface IWorkspacesRepository : IRepository<WorkspaceEntity, Guid>
{
    /// <summary>Finds the workspace a slug names, tracked for changes.</summary>
    /// <param name="slug">The workspace's slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The workspace, or <c>null</c> when none has the slug.</returns>
    Task<WorkspaceEntity?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Gets the place a workspace added now takes: one after the last.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The next order value; <c>0</c> when no workspace exists.</returns>
    Task<int> NextOrderAsync(CancellationToken ct = default);

    /// <summary>Gives each workspace the place its identifier holds in <paramref name="ids"/>, in one save.</summary>
    /// <param name="ids">Every workspace's identifier, in the new order.</param>
    /// <param name="ct">A cancellation token.</param>
    Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
}
