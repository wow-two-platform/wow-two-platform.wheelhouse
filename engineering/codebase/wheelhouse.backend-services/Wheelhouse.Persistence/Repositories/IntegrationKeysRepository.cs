using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Integrations.Entities;
using WoW.Two.Sdk.Backend.Beta.Identity.ApiKeys;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses integration keys, for the operator's list and for the API key scheme's lookups.</summary>
internal sealed class IntegrationKeysRepository(WheelhouseDbContext db) : IIntegrationKeysRepository, IApiKeyRepository
{
    /// <inheritdoc />
    public async Task AddAsync(IntegrationKeyEntity key, CancellationToken ct = default)
    {
        db.IntegrationKeys.Add(key);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntegrationKeyEntity>> ListAsync(CancellationToken ct = default) =>
        await db.IntegrationKeys.AsNoTracking()
            .OrderByDescending(key => key.CreatedAt)
            .ThenByDescending(key => key.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IntegrationKeyEntity?> FindAsync(Guid id, CancellationToken ct = default) =>
        await db.IntegrationKeys.FirstOrDefaultAsync(key => key.Id == id, ct);

    /// <inheritdoc />
    public async Task UpdateAsync(IntegrationKeyEntity key, CancellationToken ct = default)
    {
        db.IntegrationKeys.Update(key);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<ApiKeyRecord?> FindLiveByHashAsync(string hash, CancellationToken cancellationToken)
    {
        var key = await db.IntegrationKeys.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Hash == hash && item.RevokedAt == null, cancellationToken);
        return key is null
            ? null
            : new ApiKeyRecord { Id = key.Id.ToString(), Name = key.Name, LastUsedAt = key.LastUsedAt, Scopes = key.ScopeList };
    }

    /// <inheritdoc />
    public async Task TouchAsync(string id, DateTimeOffset usedAt, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var keyId))
            return;
        await db.IntegrationKeys.Where(key => key.Id == keyId)
            .ExecuteUpdateAsync(set => set.SetProperty(key => key.LastUsedAt, (DateTimeOffset?)usedAt), cancellationToken);
    }
}
