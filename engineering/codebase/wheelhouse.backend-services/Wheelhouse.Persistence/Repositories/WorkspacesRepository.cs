using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Workspaces.Entities;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Repositories;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the workspaces products are grouped into, in the operator's order.</summary>
internal sealed class WorkspacesRepository(WheelhouseDbContext db) : EfRepository<WorkspaceEntity, Guid>(db), IWorkspacesRepository
{
    /// <inheritdoc />
    public override async Task<IReadOnlyList<WorkspaceEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .OrderBy(entity => entity.Order)
            .ThenBy(entity => entity.Slug)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<WorkspaceEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(entity => entity.Slug == slug, ct);

    /// <inheritdoc />
    public async Task<int> NextOrderAsync(CancellationToken ct = default) =>
        (await Set.MaxAsync(entity => (int?)entity.Order, ct) ?? -1) + 1;

    /// <inheritdoc />
    public async Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var tracked = await Set.Where(entity => ids.Contains(entity.Id)).ToDictionaryAsync(entity => entity.Id, ct);
        for (var place = 0; place < ids.Count; place++)
            if (tracked.TryGetValue(ids[place], out var workspace))
                workspace.Order = place;
        await Context.SaveChangesAsync(ct);
    }
}
