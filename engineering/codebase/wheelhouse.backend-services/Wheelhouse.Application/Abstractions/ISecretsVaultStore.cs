using Wheelhouse.Domain.SecretsVault.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the built-in Secrets Vault's storage: the canary, the products' wrapped data keys, secrets and their
/// encrypted versions. Writes are staged and saved together, so a caller's transaction covers them.</summary>
public interface ISecretsVaultStore
{
    /// <summary>Gets the canary, staged or stored, or null before the vault's first write.</summary>
    Task<VaultCanaryEntity?> GetCanaryAsync(CancellationToken ct);

    /// <summary>Stages the canary.</summary>
    void Add(VaultCanaryEntity canary);

    /// <summary>Gets a product's wrapped data key, staged ones included.</summary>
    Task<VaultProductKeyEntity?> GetProductKeyAsync(Guid productId, CancellationToken ct);

    /// <summary>Gets every product's wrapped data key.</summary>
    Task<IReadOnlyList<VaultProductKeyEntity>> GetProductKeysAsync(CancellationToken ct);

    /// <summary>Stages a product's new data key.</summary>
    void Add(VaultProductKeyEntity key);

    /// <summary>Gets an environment's secrets, removed ones included, tracked for change.</summary>
    Task<IReadOnlyList<VaultSecretEntity>> GetSecretsAsync(Guid productId, DeploymentEnvironment environment, CancellationToken ct);

    /// <summary>Gets one secret of an environment, tracked for change; staged ones included.</summary>
    Task<VaultSecretEntity?> GetSecretAsync(Guid productId, DeploymentEnvironment environment, string key, CancellationToken ct);

    /// <summary>Gets every secret of every product, read only.</summary>
    Task<IReadOnlyList<VaultSecretEntity>> GetAllSecretsAsync(CancellationToken ct);

    /// <summary>Stages a new secret.</summary>
    void Add(VaultSecretEntity secret);

    /// <summary>Gets one version of a secret.</summary>
    Task<VaultSecretVersionEntity?> GetVersionAsync(Guid secretId, int version, CancellationToken ct);

    /// <summary>Gets a secret's versions, newest first.</summary>
    Task<IReadOnlyList<VaultSecretVersionEntity>> GetVersionsAsync(Guid secretId, CancellationToken ct);

    /// <summary>Gets the versions the given secrets are on, by secret.</summary>
    Task<IReadOnlyDictionary<Guid, VaultSecretVersionEntity>> GetCurrentVersionsAsync(
        IReadOnlyCollection<VaultSecretEntity> secrets, CancellationToken ct);

    /// <summary>Gets every version of every secret, read only, for a full verification.</summary>
    Task<IReadOnlyList<VaultSecretVersionEntity>> GetAllVersionsAsync(CancellationToken ct);

    /// <summary>Stages a new version.</summary>
    void Add(VaultSecretVersionEntity version);

    /// <summary>Saves everything staged.</summary>
    Task SaveAsync(CancellationToken ct);
}
