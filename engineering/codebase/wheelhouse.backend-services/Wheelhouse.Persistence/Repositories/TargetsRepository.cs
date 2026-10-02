using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore.Repositories;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses each product's environments on their servers.</summary>
internal sealed class TargetsRepository(WheelhouseDbContext db) : EfRepository<TargetEntity, Guid>(db), ITargetsRepository
{
    /// <inheritdoc />
    public override async Task<IReadOnlyList<TargetEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().OrderBy(entity => entity.Slug).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<TargetEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(entity => entity.Slug == slug, ct);

    /// <inheritdoc />
    public Task<bool> AnyForProductAsync(Guid productId, CancellationToken ct = default) =>
        Set.AnyAsync(entity => entity.ProductId == productId, ct);

    /// <inheritdoc />
    public Task<bool> AnyForServerAsync(Guid serverId, CancellationToken ct = default) =>
        Set.AnyAsync(entity => entity.ServerId == serverId, ct);
}
