using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.SecretsVault.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="ISecretsVaultStore"/> over the <c>vault</c> schema: lookups see staged
/// rows first, so one write sees what an earlier one in the same save staged.</summary>
internal sealed class SecretsVaultStore(WheelhouseDbContext db) : ISecretsVaultStore
{
    /// <inheritdoc />
    public async Task<VaultCanaryEntity?> GetCanaryAsync(CancellationToken ct) =>
        db.VaultCanary.Local.FirstOrDefault() ?? await db.VaultCanary.FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public void Add(VaultCanaryEntity canary) => db.VaultCanary.Add(canary);

    /// <inheritdoc />
    public async Task<VaultProductKeyEntity?> GetProductKeyAsync(Guid productId, CancellationToken ct) =>
        db.VaultProductKeys.Local.FirstOrDefault(key => key.ProductId == productId)
        ?? await db.VaultProductKeys.FirstOrDefaultAsync(key => key.ProductId == productId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<VaultProductKeyEntity>> GetProductKeysAsync(CancellationToken ct) =>
        await db.VaultProductKeys.AsNoTracking().OrderBy(key => key.CreatedAt).ToListAsync(ct);

    /// <inheritdoc />
    public void Add(VaultProductKeyEntity key) => db.VaultProductKeys.Add(key);

    /// <inheritdoc />
    public async Task<IReadOnlyList<VaultSecretEntity>> GetSecretsAsync(
        Guid productId, DeploymentEnvironment environment, CancellationToken ct) =>
        await db.VaultSecrets
            .Where(secret => secret.ProductId == productId && secret.Environment == environment)
            .OrderBy(secret => secret.Key)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<VaultSecretEntity?> GetSecretAsync(
        Guid productId, DeploymentEnvironment environment, string key, CancellationToken ct) =>
        db.VaultSecrets.Local.FirstOrDefault(secret =>
            secret.ProductId == productId && secret.Environment == environment && secret.Key == key)
        ?? await db.VaultSecrets.FirstOrDefaultAsync(secret =>
            secret.ProductId == productId && secret.Environment == environment && secret.Key == key, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<VaultSecretEntity>> GetAllSecretsAsync(CancellationToken ct) =>
        await db.VaultSecrets.AsNoTracking().OrderBy(secret => secret.Key).ToListAsync(ct);

    /// <inheritdoc />
    public void Add(VaultSecretEntity secret) => db.VaultSecrets.Add(secret);

    /// <inheritdoc />
    public async Task<VaultSecretVersionEntity?> GetVersionAsync(Guid secretId, int version, CancellationToken ct) =>
        await db.VaultSecretVersions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SecretId == secretId && item.Version == version, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<VaultSecretVersionEntity>> GetVersionsAsync(Guid secretId, CancellationToken ct) =>
        await db.VaultSecretVersions.AsNoTracking()
            .Where(item => item.SecretId == secretId)
            .OrderByDescending(item => item.Version)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, VaultSecretVersionEntity>> GetCurrentVersionsAsync(
        IReadOnlyCollection<VaultSecretEntity> secrets, CancellationToken ct)
    {
        if (secrets.Count == 0)
            return new Dictionary<Guid, VaultSecretVersionEntity>();
        var ids = secrets.Select(secret => secret.Id).ToList();
        var current = secrets.ToDictionary(secret => secret.Id, secret => secret.CurrentVersion);
        var rows = await db.VaultSecretVersions.AsNoTracking().Where(item => ids.Contains(item.SecretId)).ToListAsync(ct);
        return rows.Where(item => current[item.SecretId] == item.Version).ToDictionary(item => item.SecretId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VaultSecretVersionEntity>> GetAllVersionsAsync(CancellationToken ct) =>
        await db.VaultSecretVersions.AsNoTracking().OrderBy(item => item.SecretId).ThenBy(item => item.Version).ToListAsync(ct);

    /// <inheritdoc />
    public void Add(VaultSecretVersionEntity version) => db.VaultSecretVersions.Add(version);

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
