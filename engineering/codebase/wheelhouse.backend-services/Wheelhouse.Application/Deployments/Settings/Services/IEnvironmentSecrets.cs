using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>Defines where an environment's secrets live for its settings: the built-in Secrets Vault, or for one release
/// after the merge, the inventory's standalone vaults (<c>Vault:Mode</c> <c>external</c>).</summary>
public interface IEnvironmentSecrets
{
    /// <summary>Gets the mode: <c>builtin</c> or <c>external</c>.</summary>
    string Mode { get; }

    /// <summary>Where the environment's secrets live; null when nothing can hold them (no inventory vault).</summary>
    Task<DeploySettingsVaultDto?> PlaceAsync(TargetEntity target, ProductEntity product, CancellationToken ct);

    /// <summary>The environment's secrets with their metadata; null when the vault cannot say which it holds.</summary>
    Task<IReadOnlyDictionary<string, SecretMetadataDto>?> ListAsync(TargetEntity target, ProductEntity product, CancellationToken ct);

    /// <summary>Reads current values for a deploy's render.</summary>
    Task<AppResult<IReadOnlyDictionary<string, RenderedSecret>>> ReadAsync(
        TargetEntity target, ProductEntity product, IReadOnlyCollection<string> keys, string purpose, CancellationToken ct);

    /// <summary>Writes new versions: the built-in vault stages them for the caller's transaction, a standalone vault
    /// takes them at once.</summary>
    Task<AppResult<IReadOnlyList<SecretWritten>>> WriteAsync(
        TargetEntity target, ProductEntity product, IReadOnlyList<SecretWrite> writes, string actor, CancellationToken ct);

    /// <summary>Removes secrets; returns the keys it removed with the version each was on.</summary>
    Task<AppResult<IReadOnlyDictionary<string, int>>> RemoveAsync(
        TargetEntity target, ProductEntity product, IReadOnlyCollection<string> keys, CancellationToken ct);
}
