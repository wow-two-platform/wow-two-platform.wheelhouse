using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Vaults.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore.Repositories;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the secrets vaults Wheelhouse administers.</summary>
internal sealed class VaultsRepository(WheelhouseDbContext db) : EfRepository<VaultEntity, Guid>(db), IVaultsRepository
{
    /// <inheritdoc />
    public override async Task<IReadOnlyList<VaultEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().OrderBy(entity => entity.Slug).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<VaultEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(entity => entity.Slug == slug, ct);

    /// <inheritdoc />
    public Task<bool> AnyForServerAsync(Guid serverId, CancellationToken ct = default) =>
        Set.AnyAsync(entity => entity.ServerId == serverId, ct);
}
