using System.Security.Cryptography;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Sessions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Services;

/// <summary>Imports a standalone Secrets Vault into the built-in one: each namespace named <c>{product}-{environment}</c>
/// of a product Wheelhouse knows becomes that environment's secrets, re-encrypted under the product's data key. A secret
/// the built-in vault already holds is left as it is, so a second run imports nothing twice. Every value is decrypted
/// again before the import commits; any failure keeps nothing. Reports counts and names, never a value.</summary>
public sealed class SecretsVaultImporter(
    SecretsVaultService vault,
    ISecretsVaultStore store,
    IProductsRepository products,
    IStandaloneVaultReader reader,
    IVaultGateway gateway,
    IVaultKeySource keys,
    SecretsVaultCrypto crypto,
    IDataSession session)
{
    /// <summary>Imports every version from a standalone vault's database, under that vault's master key.</summary>
    /// <param name="connectionString">The standalone vault's database, or a restore of its dump; never logged.</param>
    /// <param name="sourceKey">The standalone vault's master key; null takes the root key file, when the owner kept the
    /// same key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>What it imported, or why it could not.</returns>
    public async Task<AppResult<VaultImportDto>> FromDatabaseAsync(string connectionString, byte[]? sourceKey, CancellationToken ct)
    {
        var master = sourceKey ?? keys.Read().Key;
        if (master is null)
            return AppResult<VaultImportDto>.Fail(SecretsVaultService.Sealed(keys.Read().Problem));
        try
        {
            var read = await reader.ReadAsync(connectionString, ct);
            var places = await PlacesAsync(read.Namespaces.Select(space => space.Slug), ct);
            var imported = new List<ImportedSecret>();
            var failures = new List<string>();
            int skipped = 0;
            foreach (var space in read.Namespaces)
            {
                if (!places.TryGetValue(space.Slug, out var place))
                    continue;
                var held = (await store.GetSecretsAsync(place.ProductId, place.Environment, ct)).Select(secret => secret.Key)
                    .ToHashSet(StringComparer.Ordinal);
                var dataKeys = new Dictionary<Guid, byte[]>();
                try
                {
                    foreach (var secret in space.Secrets)
                    {
                        if (held.Contains(secret.Key))
                        {
                            skipped++;
                            continue;
                        }
                        var versions = new List<ImportedVersion>();
                        try
                        {
                            foreach (var version in secret.Versions)
                            {
                                if (!dataKeys.TryGetValue(version.DataKeyId, out var dataKey))
                                {
                                    var wrapped = space.DataKeys.GetValueOrDefault(version.DataKeyId)
                                        ?? throw new CryptographicException("The version's data key is missing.");
                                    dataKeys[version.DataKeyId] = dataKey = crypto.Open(Payload(wrapped), master, "dek:" + space.Slug);
                                }
                                versions.Add(new ImportedVersion(version.Version,
                                    crypto.OpenValue(Payload(version.Value), dataKey, space.Slug + "/" + secret.Key),
                                    version.CreatedBy, version.CreatedAt));
                            }
                        }
                        catch (CryptographicException)
                        {
                            failures.Add(space.Slug + "/" + secret.Key);
                            continue;
                        }
                        if (versions.Count == 0 || versions.All(version => version.Version != secret.CurrentVersion))
                        {
                            failures.Add(space.Slug + "/" + secret.Key);
                            continue;
                        }
                        imported.Add(new ImportedSecret(place.ProductId, place.Environment, secret.Key, secret.Disabled,
                            secret.CurrentVersion, versions, "imported from " + space.Slug));
                    }
                }
                finally
                {
                    foreach (var key in dataKeys.Values)
                        CryptographicOperations.ZeroMemory(key);
                }
            }
            return await CommitAsync("database", places.Count, read.Namespaces.Select(space => space.Slug), places.Keys,
                imported, skipped, failures, ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
        }
    }

    /// <summary>Imports the current value of every enabled secret from an inventory vault over its management API, with
    /// Wheelhouse's administrator credential and a short-lived read token per namespace. Each arrives as version 1, its
    /// note naming the version it was there; disabled secrets stay behind, counted as skipped.</summary>
    /// <param name="vaultSlug">The inventory vault.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>What it imported, or why it could not.</returns>
    public async Task<AppResult<VaultImportDto>> FromVaultAsync(string vaultSlug, CancellationToken ct)
    {
        var listed = await gateway.ReadAsync(VaultResource.Namespaces, vaultSlug, null, ct);
        if (listed is not AppResult<JsonElement>.Success { Data: { ValueKind: JsonValueKind.Array } spaces })
            return AppResult<VaultImportDto>.Fail(listed is AppResult<JsonElement>.Failure failure
                ? failure.Error
                : AppErrorFactory.ExternalUnavailable("The vault listed no namespaces."));
        var slugs = spaces.EnumerateArray().Select(space => Text(space, "slug")).OfType<string>().ToList();
        var places = await PlacesAsync(slugs, ct);
        var imported = new List<ImportedSecret>();
        var failures = new List<string>();
        int skipped = 0;
        foreach (var (slug, place) in places)
        {
            var secrets = await gateway.ReadAsync(VaultResource.Secrets, vaultSlug, slug, ct);
            if (secrets is not AppResult<JsonElement>.Success { Data: { ValueKind: JsonValueKind.Array } items })
            {
                failures.Add(slug);
                continue;
            }
            var held = (await store.GetSecretsAsync(place.ProductId, place.Environment, ct)).Select(secret => secret.Key)
                .ToHashSet(StringComparer.Ordinal);
            var versions = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray())
            {
                if (Text(item, "key") is not { } key)
                    continue;
                var disabled = item.TryGetProperty("state", out var state)
                    && (state.ValueKind == JsonValueKind.String && string.Equals(state.GetString(), "disabled", StringComparison.OrdinalIgnoreCase)
                        || state.ValueKind == JsonValueKind.Number && state.TryGetInt32(out var number) && number == 1);
                if (held.Contains(key) || disabled)
                {
                    skipped++;
                    continue;
                }
                versions[key] = item.TryGetProperty("version", out var version) && version.TryGetInt32(out var value) ? value : 0;
            }
            if (versions.Count == 0)
                continue;
            var read = await gateway.ReadValuesAsync(vaultSlug, slug, [.. versions.Keys], "import into the built-in vault", ct);
            if (read is not AppResult<IReadOnlyDictionary<string, string>>.Success { Data: var values })
            {
                failures.Add(slug);
                continue;
            }
            foreach (var (key, number) in versions)
            {
                if (!values.TryGetValue(key, out var value))
                {
                    failures.Add(slug + "/" + key);
                    continue;
                }
                imported.Add(new ImportedSecret(place.ProductId, place.Environment, key, false, 1,
                    [new ImportedVersion(1, value, "import", default)],
                    "imported from " + vaultSlug + "/" + slug + (number > 0 ? " v" + number : "")));
            }
        }
        return await CommitAsync(vaultSlug, places.Count, slugs, places.Keys, imported, skipped, failures, ct);
    }

    /// <summary>Stages, verifies and commits the import in one transaction; any failure keeps nothing. @internal</summary>
    private async Task<AppResult<VaultImportDto>> CommitAsync(
        string source, int matched, IEnumerable<string> all, IEnumerable<string> placed, IReadOnlyList<ImportedSecret> imported,
        int skipped, List<string> failures, CancellationToken ct)
    {
        var unmatched = all.Except(placed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        int versions = 0, verified = 0;
        if (imported.Count > 0 && failures.Count == 0)
        {
            // Completing the unit saves what was staged and commits; leaving it without completing keeps nothing.
            await using var unit = await session.BeginAsync(ct);
            var staged = await vault.StageImportAsync(imported, ct);
            if (staged is AppResult<(int Versions, int Verified, IReadOnlyList<string> Failures)>.Failure refused)
                return AppResult<VaultImportDto>.Fail(refused.Error);
            (versions, verified, var stagedFailures) = ((AppResult<(int Versions, int Verified, IReadOnlyList<string> Failures)>.Success)staged).Data;
            failures.AddRange(stagedFailures);
            if (failures.Count == 0)
                await unit.CompleteAsync(ct);
        }
        var kept = failures.Count == 0;
        return AppResult<VaultImportDto>.Ok(new VaultImportDto(source, matched, unmatched, kept ? imported.Count : 0,
            kept ? versions : 0, skipped, kept ? verified : 0, failures.Count, [.. failures.Take(50)]));
    }

    /// <summary>The product environment each namespace names, for the products Wheelhouse knows. @internal</summary>
    private async Task<Dictionary<string, (Guid ProductId, DeploymentEnvironment Environment)>> PlacesAsync(
        IEnumerable<string> slugs, CancellationToken ct)
    {
        var bySlug = (await products.GetAllAsync(ct)).ToDictionary(product => product.Slug, product => product.Id, StringComparer.Ordinal);
        var result = new Dictionary<string, (Guid, DeploymentEnvironment)>(StringComparer.Ordinal);
        foreach (var slug in slugs)
        {
            var cut = slug.LastIndexOf('-');
            if (cut <= 0)
                continue;
            DeploymentEnvironment? environment = slug[(cut + 1)..] switch
            {
                "dev" => DeploymentEnvironment.Dev,
                "prod" => DeploymentEnvironment.Prod,
                _ => null,
            };
            if (environment is { } found && bySlug.TryGetValue(slug[..cut], out var product))
                result[slug] = (product, found);
        }
        return result;
    }

    private static EncryptedPayload Payload(StandaloneSealed sealedValue) =>
        new() { CipherText = sealedValue.CipherText, Nonce = sealedValue.Nonce, Tag = sealedValue.Tag };

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
