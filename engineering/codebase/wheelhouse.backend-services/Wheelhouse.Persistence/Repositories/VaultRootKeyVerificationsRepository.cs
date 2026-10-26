using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.SecretsVault.Entities;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IVaultRootKeyVerifications"/> over the <c>vault</c> schema.</summary>
internal sealed class VaultRootKeyVerificationsRepository(WheelhouseDbContext db) : IVaultRootKeyVerifications
{
    /// <inheritdoc />
    public async Task<VaultRootKeyVerificationEntity?> GetLatestAsync(CancellationToken ct) =>
        await db.Set<VaultRootKeyVerificationEntity>().AsNoTracking()
            .OrderByDescending(entity => entity.VerifiedAt).ThenByDescending(entity => entity.Id).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task AddAsync(VaultRootKeyVerificationEntity verification, CancellationToken ct)
    {
        db.Set<VaultRootKeyVerificationEntity>().Add(verification);
        await db.SaveChangesAsync(ct);
    }
}
