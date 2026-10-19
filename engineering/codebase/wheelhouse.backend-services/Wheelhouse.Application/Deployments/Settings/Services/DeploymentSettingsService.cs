using System.Text.Json;
using System.Text.RegularExpressions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Constants;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>Joins what a release's services take on a target (read from the runner) with where the next deploy's render
/// takes each value from: what it derives, the environment's vault namespace, Wheelhouse's settings store, a release
/// default or the target's host file. Builds the values a deploy sends the target and the keys a check weighs.</summary>
/// <remarks>
/// An environment's secrets live in the vault on its target's server, else in the inventory's first vault (the control
/// host's), under <c>{product}-{environment}</c>. Values never reach a log, a record or the browser: the dialog sees key
/// names and non-secret values only, and a deploy's values go to the runner's stdin.
/// </remarks>
public sealed partial class DeploymentSettingsService(
    IDeploymentGateway gateway,
    ITargetsRepository targets,
    IProductsRepository products,
    IEnvironmentSettingsRepository store,
    IVaultsRepository vaults,
    IVaultGateway vaultGateway,
    IDeploymentRecords records)
{
    /// <summary>Holds the keys every render derives, which no stored setting or secret may set.</summary>
    public static readonly IReadOnlySet<string> DerivedKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AllowedHosts", "Deployment:TrustedProxies" };

    /// <summary>Whether a key is one the render always derives: <c>AllowedHosts</c>, <c>Deployment:TrustedProxies</c> or a
    /// <c>Sites:&lt;site&gt;:Url</c>.</summary>
    /// <param name="key">The configuration path.</param>
    /// <returns><c>true</c> when the render owns it.</returns>
    public static bool IsDerived(string key) => DerivedKeys.Contains(key) || SiteUrl().IsMatch(key);

    /// <summary>Whether a key is a configuration path the runner accepts: sections joined by <c>:</c>.</summary>
    /// <param name="key">The key.</param>
    /// <returns><c>true</c> when it is one.</returns>
    public static bool IsKey(string? key) => key is not null && Key().IsMatch(key);

    /// <summary>Reads the settings a release takes on a target and where the next deploy's render takes each from.</summary>
    /// <param name="targetSlug">The target's slug.</param>
    /// <param name="release">The release bundle's identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The plan, or why it could not be read.</returns>
    public async Task<AppResult<DeploySettingsDto>> PlanAsync(string targetSlug, string release, CancellationToken ct)
    {
        var target = await targets.GetBySlugAsync(targetSlug, ct);
        if (target is null)
            return AppResult<DeploySettingsDto>.Fail(AppErrorFactory.NotFound($"Target '{targetSlug}' was not found."));
        var product = await products.GetByIdAsync(target.ProductId, ct);
        if (product is null)
            return AppResult<DeploySettingsDto>.Fail(AppErrorFactory.NotFound($"Target '{targetSlug}' has no product."));
        var read = await gateway.SettingsAsync(targetSlug, release, ct);
        if (read is AppResult<JsonElement>.Failure failure)
            return AppResult<DeploySettingsDto>.Fail(failure.Error);
        var declared = ((AppResult<JsonElement>.Success)read).Data;

        var stored = (await store.GetAsync(product.Id, target.Environment, ct)).ToDictionary(row => row.Key, row => row.Value);
        var place = await VaultOfAsync(target, product, ct);
        var secrets = place is null ? new HashSet<string>() : await SecretKeysAsync(place.Value.Vault, place.Value.Namespace, ct);
        var services = Services(declared, stored, secrets ?? new HashSet<string>());
        return AppResult<DeploySettingsDto>.Ok(new DeploySettingsDto(
            targetSlug, product.Slug, EnvironmentName(target.Environment), Text(declared, "release") ?? release,
            await FirstDeployAsync(targetSlug, ct),
            place is null ? null : new DeploySettingsVaultDto(place.Value.Vault, place.Value.Namespace, secrets is not null),
            services,
            services.Where(service => !service.HostFile).Sum(service => service.Keys.Count(key => key.Source == SettingSources.Missing))));
    }

    /// <summary>The values a deploy sends its target: the environment's stored settings, then its vault secrets over
    /// them, read with a short-lived token. Refused while the environment's vault cannot be read, so a render never
    /// drops a secret a service had.</summary>
    /// <param name="target">The target.</param>
    /// <param name="product">The target's product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>{"values": {key: value}}</c>, or why the values could not be read.</returns>
    public async Task<AppResult<JsonElement>> ValuesAsync(TargetEntity target, ProductEntity product, CancellationToken ct)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in await store.GetAsync(product.Id, target.Environment, ct))
            if (IsKey(row.Key) && !IsDerived(row.Key))
                values[row.Key] = row.Value;
        if (await VaultOfAsync(target, product, ct) is { } place)
        {
            var keys = await SecretKeysAsync(place.Vault, place.Namespace, ct);
            if (keys is null)
                return AppResult<JsonElement>.Fail(AppErrorFactory.ExternalUnavailable(
                    $"The vault {place.Vault} is unreachable, so {target.Slug} cannot render its secrets; deploy once it answers."));
            var wanted = keys.Where(key => IsKey(key) && !IsDerived(key)).ToList();
            if (wanted.Count > 0)
            {
                var read = await vaultGateway.ReadValuesAsync(place.Vault, place.Namespace, wanted, "deploy " + target.Slug, ct);
                if (read is not AppResult<IReadOnlyDictionary<string, string>>.Success { Data: var secrets })
                    return AppResult<JsonElement>.Fail(AppErrorFactory.ExternalUnavailable(
                        $"The vault {place.Vault} refused to read {target.Slug}'s secrets; deploy once it does."));
                foreach (var (key, value) in secrets)
                    values[key] = value;
            }
        }
        return AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new { values }));
    }

    /// <summary>The setting keys Wheelhouse would send a target, without values, for its readiness check; null when the
    /// vault cannot say which secrets it holds.</summary>
    /// <param name="target">The target.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The keys, or null when unknown.</returns>
    public async Task<IReadOnlyCollection<string>?> KeysAsync(TargetEntity target, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(target.ProductId, ct);
        if (product is null)
            return null;
        var keys = (await store.GetAsync(product.Id, target.Environment, ct)).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        if (await VaultOfAsync(target, product, ct) is { } place)
        {
            if (await SecretKeysAsync(place.Vault, place.Namespace, ct) is not { } secrets)
                return null;
            keys.UnionWith(secrets);
        }
        return [.. keys.Where(IsKey).Order(StringComparer.Ordinal)];
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
        return vault is null ? null : (vault.Slug, product.Slug + "-" + EnvironmentName(target.Environment));
    }

    /// <summary>The environment as targets and namespaces name it: <c>dev</c> or <c>prod</c>.</summary>
    /// <param name="environment">The environment.</param>
    /// <returns>Its name.</returns>
    public static string EnvironmentName(DeploymentEnvironment environment) => environment.ToString().ToLowerInvariant();

    /// <summary>The enabled secret keys of a namespace: empty when the namespace does not exist yet, null when the vault
    /// cannot be read. @internal</summary>
    private async Task<HashSet<string>?> SecretKeysAsync(string vault, string ns, CancellationToken ct)
    {
        var result = await vaultGateway.ReadAsync(VaultResource.Secrets, vault, ns, ct);
        if (result is AppResult<JsonElement>.Failure { Error.Type: AppErrorType.NotFound })
            return [];
        if (result is not AppResult<JsonElement>.Success { Data: { ValueKind: JsonValueKind.Array } items })
            return null;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            if (Text(item, "key") is not { } key)
                continue;
            var state = item.TryGetProperty("state", out var value) ? value : default;
            var disabled = state.ValueKind switch
            {
                JsonValueKind.String => string.Equals(state.GetString(), "disabled", StringComparison.OrdinalIgnoreCase),
                JsonValueKind.Number => state.TryGetInt32(out var number) && number == 1,
                _ => false,
            };
            if (!disabled)
                keys.Add(key);
        }
        return keys;
    }

    /// <summary>Whether no deploy has succeeded on a target yet. @internal</summary>
    private async Task<bool> FirstDeployAsync(string targetSlug, CancellationToken ct) =>
        await records.ListAsync(ct) is AppResult<IReadOnlyList<History.Models.DeploymentRecord>>.Success { Data: var list }
        && !list.Any(record => record.TargetId == targetSlug && record.Status == "succeeded");

    /// <summary>Reads the runner's declarations into services, each key with its source. @internal</summary>
    private static List<DeploySettingsServiceDto> Services(
        JsonElement declared, IReadOnlyDictionary<string, string> stored, IReadOnlySet<string> secrets)
    {
        var result = new List<DeploySettingsServiceDto>();
        if (!declared.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var service in services.EnumerateArray())
        {
            if (Text(service, "name") is not { } name)
                continue;
            var hostFile = service.TryGetProperty("hostFile", out var host) && host.ValueKind == JsonValueKind.True;
            var keys = new List<DeploySettingDto>();
            foreach (var item in service.TryGetProperty("keys", out var list) && list.ValueKind == JsonValueKind.Array
                         ? list.EnumerateArray() : default(JsonElement.ArrayEnumerator))
            {
                if (Text(item, "key") is not { } key)
                    continue;
                var required = item.TryGetProperty("required", out var flag) && flag.ValueKind == JsonValueKind.True;
                var secret = item.TryGetProperty("secret", out var kind) && kind.ValueKind == JsonValueKind.True;
                var derived = Text(item, "derived");
                var fallback = Text(item, "default");
                IReadOnlyList<string>? allowed = item.TryGetProperty("allowed", out var choices) && choices.ValueKind == JsonValueKind.Array
                    ? [.. choices.EnumerateArray().Where(choice => choice.ValueKind == JsonValueKind.String).Select(choice => choice.GetString()!)]
                    : null;
                // A derived database string fills only a key no layer sets; every other derived key wins over the layers.
                var (source, value) = derived is not null && (derived != "database" || (!secrets.Contains(key) && !stored.ContainsKey(key)))
                        ? (SettingSources.Derived, Text(item, "value"))
                    : secrets.Contains(key) ? (SettingSources.Vault, null)
                    : stored.TryGetValue(key, out var kept) ? (SettingSources.Stored, secret ? null : kept)
                    : fallback is not null ? (SettingSources.Default, fallback)
                    : required ? (hostFile ? SettingSources.Host : SettingSources.Missing, null)
                    : (SettingSources.Unset, (string?)null);
                keys.Add(new DeploySettingDto(key, required, secret, source, value, fallback, derived, allowed));
            }
            result.Add(new DeploySettingsServiceDto(name, hostFile, keys));
        }
        return result;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A configuration path the runner accepts, as its <c>SETTING_KEY</c>.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}(?::[A-Za-z0-9_.-]{1,100}){0,9}$")]
    private static partial Regex Key();

    /// <summary>A site URL the render derives: <c>Sites:&lt;site&gt;:Url</c>.</summary>
    [GeneratedRegex(@"^Sites:[^:]+:Url$", RegexOptions.IgnoreCase)]
    private static partial Regex SiteUrl();
}
