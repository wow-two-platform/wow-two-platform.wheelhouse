using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the holds on deploys.</summary>
internal sealed class DeployFreezesRepository(WheelhouseDbContext db) : IDeployFreezesRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DeployFreezeEntity>> GetAllAsync(CancellationToken ct = default) =>
        (await db.Set<DeployFreezeEntity>().AsNoTracking().ToListAsync(ct)).OrderBy(entity => entity.CreatedAt).ToList();

    /// <inheritdoc />
    public Task<DeployFreezeEntity?> FindAsync(Guid? productId, DeploymentEnvironment? environment, CancellationToken ct = default) =>
        db.Set<DeployFreezeEntity>().AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.ProductId == productId && entity.Environment == environment, ct);

    /// <inheritdoc />
    public async Task<DeployFreezeEntity> AddAsync(DeployFreezeEntity freeze, CancellationToken ct = default)
    {
        db.Set<DeployFreezeEntity>().Add(freeze);
        await db.SaveChangesAsync(ct);
        return freeze;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid id, CancellationToken ct = default) =>
        await db.Set<DeployFreezeEntity>().Where(entity => entity.Id == id).ExecuteDeleteAsync(ct);
}
