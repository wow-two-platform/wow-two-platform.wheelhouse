using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.SecretsVault.Constants;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Application.Vaults.Changes;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>The fallback for one release after the merge (<c>Vault:Mode</c> <c>external</c>): an environment's secrets
/// live in the vault on its target's server, else the inventory's first vault, under <c>{product}-{environment}</c>,
/// reached over the management API with the administrator password file. Writes go to the vault at once, so they
/// share no transaction with the settings; versions and fingerprints stay with that vault.</summary>
public sealed class ExternalEnvironmentSecrets(IVaultsRepository vaults, IVaultGateway gateway) : IEnvironmentSecrets
{
    /// <inheritdoc />
    public string Mode => SecretsVaultModes.External;

    /// <inheritdoc />
    public async Task<DeploySettingsVaultDto?> PlaceAsync(TargetEntity target, ProductEntity product, CancellationToken ct)
    {
        if (await VaultOfAsync(target, product, ct) is not { } place)
            return null;
        return new DeploySettingsVaultDto(place.Vault, place.Namespace, await ListAsync(target, product, ct) is not null) { Mode = Mode };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, SecretMetadataDto>?> ListAsync(
        TargetEntity target, ProductEntity product, CancellationToken ct)
    {
        if (await VaultOfAsync(target, product, ct) is not { } place)
            return new Dictionary<string, SecretMetadataDto>();
        var result = await gateway.ReadAsync(VaultResource.Secrets, place.Vault, place.Namespace, ct);
        if (result is AppResult<JsonElement>.Failure { Error.Type: AppErrorType.NotFound })
            return new Dictionary<string, SecretMetadataDto>();
        if (result is not AppResult<JsonElement>.Success { Data: { ValueKind: JsonValueKind.Array } items })
            return null;
        var keys = new SortedDictionary<string, SecretMetadataDto>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            if (Text(item, "key") is not { } key || Disabled(item))
                continue;
            var version = item.TryGetProperty("version", out var number) && number.TryGetInt32(out var value) ? value : 0;
            var updated = item.TryGetProperty("updatedAtUtc", out var at) && at.TryGetDateTimeOffset(out var when) ? when : default;
            keys[key] = new SecretMetadataDto(key, version, updated, "", "", null, null);
        }
        return keys;
    }

    /// <inheritdoc />
    public async Task<AppResult<IReadOnlyDictionary<string, RenderedSecret>>> ReadAsync(
        TargetEntity target, ProductEntity product, IReadOnlyCollection<string> keys, string purpose, CancellationToken ct)
    {
        if (keys.Count == 0 || await VaultOfAsync(target, product, ct) is not { } place)
            return AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Ok(new Dictionary<string, RenderedSecret>());
        var read = await gateway.ReadValuesAsync(place.Vault, place.Namespace, keys, purpose, ct);
        if (read is not AppResult<IReadOnlyDictionary<string, string>>.Success { Data: var values })
            return AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Fail(AppErrorFactory.ExternalUnavailable(
                $"The vault {place.Vault} refused to read {target.Slug}'s secrets; deploy once it does."));
        return AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Ok(
            values.ToDictionary(pair => pair.Key, pair => new RenderedSecret(pair.Value, null, null), StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public async Task<AppResult<IReadOnlyList<SecretWritten>>> WriteAsync(
        TargetEntity target, ProductEntity product, IReadOnlyList<SecretWrite> writes, string actor, CancellationToken ct)
    {
        if (writes.Count == 0)
            return AppResult<IReadOnlyList<SecretWritten>>.Ok([]);
        if (await VaultOfAsync(target, product, ct) is not { } place)
            return AppResult<IReadOnlyList<SecretWritten>>.Fail(AppErrorFactory.Conflict(
                "No vault holds this environment's secrets yet; add a vault under Servers first."));
        var created = false;
        var written = new List<SecretWritten>();
        foreach (var write in writes)
        {
            var change = new SecretSetChange { Namespace = place.Namespace, Key = write.Key, Value = write.Value, Description = write.Note };
            var result = await gateway.ApplyAsync(place.Vault, change, ct);
            if (result is AppResult<JsonElement>.Failure { Error.Type: AppErrorType.NotFound } && !created)
            {
                // The environment's first secret: its namespace comes with it.
                created = true;
                var space = await gateway.ApplyAsync(place.Vault,
                    new NamespaceCreateChange { Namespace = place.Namespace, Name = product.Name + " " + DeploymentSettingsService.EnvironmentName(target.Environment) },
                    ct);
                if (space is AppResult<JsonElement>.Failure { Error.Type: not AppErrorType.Conflict } refused)
                    return AppResult<IReadOnlyList<SecretWritten>>.Fail(refused.Error);
                result = await gateway.ApplyAsync(place.Vault, change, ct);
            }
            if (result is AppResult<JsonElement>.Failure failure)
                return AppResult<IReadOnlyList<SecretWritten>>.Fail(failure.Error);
            var data = ((AppResult<JsonElement>.Success)result).Data;
            var version = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("version", out var number)
                && number.TryGetInt32(out var value) ? value : 0;
            written.Add(new SecretWritten(write.Key, version, ""));
        }
        return AppResult<IReadOnlyList<SecretWritten>>.Ok(written);
    }

    /// <inheritdoc />
    public async Task<AppResult<IReadOnlyDictionary<string, int>>> RemoveAsync(
        TargetEntity target, ProductEntity product, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        var removed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (keys.Count == 0 || await VaultOfAsync(target, product, ct) is not { } place)
            return AppResult<IReadOnlyDictionary<string, int>>.Ok(removed);
        var held = await ListAsync(target, product, ct) ?? new Dictionary<string, SecretMetadataDto>();
        foreach (var key in keys.Where(held.ContainsKey))
        {
            var result = await gateway.ApplyAsync(place.Vault,
                new SecretStateChange { Namespace = place.Namespace, Key = key, Disabled = true }, ct);
            if (result is AppResult<JsonElement>.Failure failure)
                return AppResult<IReadOnlyDictionary<string, int>>.Fail(failure.Error);
            removed[key] = held[key].Version;
        }
        return AppResult<IReadOnlyDictionary<string, int>>.Ok(removed);
    }

    /// <summary>Where an environment's secrets live: the vault on its target's server, else the inventory's first vault
    /// (the control host's), under <c>{product}-{environment}</c>. Null when the inventory has no vault.</summary>
    /// <param name="target">The target.</param>
    /// <param name="product">The target's product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The vault and namespace, or null.</returns>
    public async Task<(string Vault, string Namespace)?> VaultOfAsync(TargetEntity target, ProductEntity product, CancellationToken ct)
    {
        var all = await vaults.GetAllAsync(ct);
        var vault = all.FirstOrDefault(item => item.ServerId == target.ServerId) ?? all.OrderBy(item => item.Slug, StringComparer.Ordinal).FirstOrDefault();
        return vault is null ? null : (vault.Slug, product.Slug + "-" + DeploymentSettingsService.EnvironmentName(target.Environment));
    }

    private static bool Disabled(JsonElement item)
    {
        var state = item.TryGetProperty("state", out var value) ? value : default;
        return state.ValueKind switch
        {
            JsonValueKind.String => string.Equals(state.GetString(), "disabled", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => state.TryGetInt32(out var number) && number == 1,
            _ => false,
        };
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
