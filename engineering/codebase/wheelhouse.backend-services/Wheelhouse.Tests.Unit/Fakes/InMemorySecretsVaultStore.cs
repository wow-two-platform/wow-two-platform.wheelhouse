using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.SecretsVault.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Keeps the built-in vault's rows in memory: staged rows count as saved, as one save would make them.</summary>
public sealed class InMemorySecretsVaultStore : ISecretsVaultStore
{
    public VaultCanaryEntity? Canary { get; set; }
    public List<VaultProductKeyEntity> Keys { get; } = [];
    public List<VaultSecretEntity> Secrets { get; } = [];
    public List<VaultSecretVersionEntity> Versions { get; } = [];
    public int Saves { get; private set; }

    public Task<VaultCanaryEntity?> GetCanaryAsync(CancellationToken ct) => Task.FromResult(Canary);
    public void Add(VaultCanaryEntity canary) => Canary = canary;
    public Task<VaultProductKeyEntity?> GetProductKeyAsync(Guid productId, CancellationToken ct) =>
        Task.FromResult(Keys.FirstOrDefault(key => key.ProductId == productId));
    public Task<IReadOnlyList<VaultProductKeyEntity>> GetProductKeysAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VaultProductKeyEntity>>([.. Keys]);
    public void Add(VaultProductKeyEntity key) => Keys.Add(key);
    public Task<IReadOnlyList<VaultSecretEntity>> GetSecretsAsync(Guid productId, DeploymentEnvironment environment, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VaultSecretEntity>>([.. Secrets.Where(secret => secret.ProductId == productId && secret.Environment == environment)]);
    public Task<VaultSecretEntity?> GetSecretAsync(Guid productId, DeploymentEnvironment environment, string key, CancellationToken ct) =>
        Task.FromResult(Secrets.FirstOrDefault(secret => secret.ProductId == productId && secret.Environment == environment && secret.Key == key));
    public Task<IReadOnlyList<VaultSecretEntity>> GetAllSecretsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VaultSecretEntity>>([.. Secrets]);
    public void Add(VaultSecretEntity secret) => Secrets.Add(secret);
    public Task<VaultSecretVersionEntity?> GetVersionAsync(Guid secretId, int version, CancellationToken ct) =>
        Task.FromResult(Versions.FirstOrDefault(item => item.SecretId == secretId && item.Version == version));
    public Task<IReadOnlyList<VaultSecretVersionEntity>> GetVersionsAsync(Guid secretId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VaultSecretVersionEntity>>([.. Versions.Where(item => item.SecretId == secretId).OrderByDescending(item => item.Version)]);
    public Task<IReadOnlyDictionary<Guid, VaultSecretVersionEntity>> GetCurrentVersionsAsync(
        IReadOnlyCollection<VaultSecretEntity> secrets, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, VaultSecretVersionEntity>>(secrets
            .Select(secret => Versions.FirstOrDefault(item => item.SecretId == secret.Id && item.Version == secret.CurrentVersion))
            .OfType<VaultSecretVersionEntity>()
            .ToDictionary(item => item.SecretId));
    public Task<IReadOnlyList<VaultSecretVersionEntity>> GetAllVersionsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VaultSecretVersionEntity>>([.. Versions]);
    public void Add(VaultSecretVersionEntity version) => Versions.Add(version);

    public Task SaveAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
