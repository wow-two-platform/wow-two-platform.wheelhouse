using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Servers.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore.Repositories;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the hosts Wheelhouse deploys to.</summary>
internal sealed class ServersRepository(WheelhouseDbContext db) : EfRepository<ServerEntity, Guid>(db), IServersRepository
{
    /// <inheritdoc />
    public override async Task<IReadOnlyList<ServerEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().OrderBy(entity => entity.Slug).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ServerEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(entity => entity.Slug == slug, ct);
}
