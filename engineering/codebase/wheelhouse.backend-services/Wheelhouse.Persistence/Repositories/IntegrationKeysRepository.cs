using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Integrations.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.EntityFrameworkCore.Repositories;
using WoW.Two.Sdk.Backend.Beta.Identity.ApiKeys;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses integration keys, for the operator's list and for the API key scheme's lookups.</summary>
internal sealed class IntegrationKeysRepository(WheelhouseDbContext db)
    : EfRepository<IntegrationKeyEntity, Guid>(db), IIntegrationKeysRepository, IApiKeyRepository
{
    /// <inheritdoc />
    public override async Task<IReadOnlyList<IntegrationKeyEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .OrderByDescending(key => key.CreatedAt)
            .ThenByDescending(key => key.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ApiKeyRecord?> FindLiveByHashAsync(string hash, CancellationToken cancellationToken)
    {
        var key = await Set.AsNoTracking()
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
        await Set.Where(key => key.Id == keyId)
            .ExecuteUpdateAsync(set => set.SetProperty(key => key.LastUsedAt, (DateTimeOffset?)usedAt), cancellationToken);
    }
}
