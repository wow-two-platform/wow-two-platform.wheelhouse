using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.SecretsVault.Constants;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>An environment's secrets in the built-in Secrets Vault: in-process, in Wheelhouse's database, staged for the
/// caller's transaction; values decrypt only for a render.</summary>
public sealed class BuiltInEnvironmentSecrets(SecretsVaultService vault) : IEnvironmentSecrets
{
    /// <inheritdoc />
    public string Mode => SecretsVaultModes.BuiltIn;

    /// <inheritdoc />
    public async Task<DeploySettingsVaultDto?> PlaceAsync(TargetEntity target, ProductEntity product, CancellationToken ct)
    {
        var (root, reason) = await vault.UnsealAsync(ct);
        if (root is not null)
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(root);
        return new DeploySettingsVaultDto("built-in",
            product.Slug + "-" + DeploymentSettingsService.EnvironmentName(target.Environment), reason is null)
        {
            Mode = Mode,
            Sealed = reason,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, SecretMetadataDto>?> ListAsync(
        TargetEntity target, ProductEntity product, CancellationToken ct) =>
        await vault.ListAsync(product.Id, target.Environment, ct);

    /// <inheritdoc />
    public Task<AppResult<IReadOnlyDictionary<string, RenderedSecret>>> ReadAsync(
        TargetEntity target, ProductEntity product, IReadOnlyCollection<string> keys, string purpose, CancellationToken ct) =>
        vault.ReadAsync(product.Id, target.Environment, keys, ct);

    /// <inheritdoc />
    public Task<AppResult<IReadOnlyList<SecretWritten>>> WriteAsync(
        TargetEntity target, ProductEntity product, IReadOnlyList<SecretWrite> writes, string actor, CancellationToken ct) =>
        vault.StageWritesAsync(product.Id, target.Environment, writes, actor, null, ct);

    /// <inheritdoc />
    public async Task<AppResult<IReadOnlyDictionary<string, int>>> RemoveAsync(
        TargetEntity target, ProductEntity product, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        var removed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in keys)
            if (await vault.StageRemoveAsync(product.Id, target.Environment, key, ct) is { } version)
                removed[key] = version;
        return AppResult<IReadOnlyDictionary<string, int>>.Ok(removed);
    }
}
