using System.Text.Json;
using System.Text.RegularExpressions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Constants;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Settings.Enums;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>Joins what a release's services take on a target (read from the runner) with where the next deploy's render
/// takes each value from: what it derives, the environment's secrets, Wheelhouse's settings store, a release default or
/// the target's host file. Builds the values a deploy sends the target, records what each render held, and the keys a
/// check weighs.</summary>
/// <remarks>
/// An environment's secrets live in the built-in Secrets Vault, under the product's data key (or, while the fallback
/// is on, in a standalone vault: <see cref="IEnvironmentSecrets"/>). Values never reach a log, a record or the browser:
/// the console sees key names, non-secret values and secret metadata, and a deploy's values go to the runner's stdin.
/// </remarks>
public sealed partial class DeploymentSettingsService(
    IDeploymentGateway gateway,
    ITargetsRepository targets,
    IProductsRepository products,
    IEnvironmentSettingsRepository store,
    IEnvironmentSecrets secrets,
    IProductSettingKeysRepository added,
    IDeploymentRecords records,
    IDeploymentRenders renders,
    ISettingsApplies applies)
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

    /// <summary>The environment as targets and namespaces name it: <c>dev</c> or <c>prod</c>.</summary>
    /// <param name="environment">The environment.</param>
    /// <returns>Its name.</returns>
    public static string EnvironmentName(DeploymentEnvironment environment) => environment.ToString().ToLowerInvariant();

    /// <summary>Reads the settings a release takes on a target and where the next deploy's render takes each from. Without
    /// a release, reads the one the target runs; a target that runs none lists only the keys the owner added or stored.</summary>
    /// <param name="targetSlug">The target's slug.</param>
    /// <param name="release">The release bundle's identifier; null for the one the target runs.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The plan, or why it could not be read.</returns>
    public async Task<AppResult<DeploySettingsDto>> PlanAsync(string targetSlug, string? release, CancellationToken ct)
    {
        var target = await targets.GetBySlugAsync(targetSlug, ct);
        if (target is null)
            return AppResult<DeploySettingsDto>.Fail(AppErrorFactory.NotFound($"Target '{targetSlug}' was not found."));
        var product = await products.GetByIdAsync(target.ProductId, ct);
        if (product is null)
            return AppResult<DeploySettingsDto>.Fail(AppErrorFactory.NotFound($"Target '{targetSlug}' has no product."));
        var read = await HistoryAsync(ct);
        var history = read ?? [];
        var bundle = release ?? CurrentBundle(history, targetSlug);
        var declared = Empty;
        if (bundle is not null)
        {
            var settings = await gateway.SettingsAsync(targetSlug, bundle, ct);
            if (settings is AppResult<JsonElement>.Failure failure && release is not null)
                return AppResult<DeploySettingsDto>.Fail(failure.Error);
            if (settings is AppResult<JsonElement>.Success { Data: var data })
                declared = data;
        }

        var stored = (await store.GetAsync(product.Id, target.Environment, ct)).ToDictionary(row => row.Key, row => row.Value);
        var place = await secrets.PlaceAsync(target, product, ct);
        var held = await secrets.ListAsync(target, product, ct);
        var adHoc = await added.GetAsync(product.Id, ct);
        var services = Services(declared, stored, held ?? new Dictionary<string, SecretMetadataDto>(), adHoc);
        var named = services.SelectMany(service => service.Keys).Select(key => key.Key).ToHashSet(StringComparer.Ordinal);
        var extra = Extra(named, stored, held ?? new Dictionary<string, SecretMetadataDto>(), adHoc);
        var pending = await applies.GetAsync(target.Id, ct);
        var label = bundle is null ? null : Text(declared, "release") ?? release ?? Label(history, bundle);
        return AppResult<DeploySettingsDto>.Ok(new DeploySettingsDto(
            targetSlug, product.Slug, EnvironmentName(target.Environment), label,
            read is not null && !history.Any(record => record.TargetId == targetSlug && record.Status == "succeeded"),
            place is null ? null : place with { Reachable = place.Reachable && held is not null },
            services,
            services.Where(service => !service.HostFile).Sum(service => service.Keys.Count(key => key.Source == SettingSources.Missing))
            + extra.Count(key => key.Source == SettingSources.Missing))
        {
            Bundle = bundle,
            Extra = extra,
            Unapplied = await UnappliedAsync(targetSlug, stored, held, ct),
            Pending = pending is null ? null : new SettingsApplyDto(targetSlug, "scheduled", pending.At, pending.RequestedBy, null, label),
        });
    }

    /// <summary>What the release (or the target's running one) and the owner declare about each key: its type, choices
    /// and whether it is a secret. An unreadable release leaves only the owner's keys.</summary>
    /// <param name="target">The target.</param>
    /// <param name="product">The target's product.</param>
    /// <param name="release">The release bundle; null for the one the target runs.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each declared key.</returns>
    public async Task<IReadOnlyDictionary<string, KeyDeclaration>> DeclarationsAsync(
        TargetEntity target, ProductEntity product, string? release, CancellationToken ct)
    {
        var result = new Dictionary<string, KeyDeclaration>(StringComparer.Ordinal);
        foreach (var key in await added.GetAsync(product.Id, ct))
            result[key.Key] = Declare(key);
        var bundle = release ?? CurrentBundle(await HistoryAsync(ct) ?? [], target.Slug);
        if (bundle is null || await gateway.SettingsAsync(target.Slug, bundle, ct) is not AppResult<JsonElement>.Success { Data: var declared }
            || !declared.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var item in services.EnumerateArray().SelectMany(service =>
                     service.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array ? keys.EnumerateArray() : default(JsonElement.ArrayEnumerator)))
        {
            if (Text(item, "key") is not { } key || Text(item, "derived") is not null)
                continue;
            result[key] = Declare(item, key);
        }
        return result;
    }

    /// <summary>The values a deploy sends its target: the environment's stored settings, then its vault secrets over
    /// them, with what each key was. Refused while its secrets cannot be read (a sealed or unreachable vault), so a render
    /// never drops a secret a service had.</summary>
    /// <param name="target">The target.</param>
    /// <param name="product">The target's product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The values and their fingerprints, or why they could not be read.</returns>
    public async Task<AppResult<RenderValues>> ValuesAsync(TargetEntity target, ProductEntity product, CancellationToken ct)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var keys = new SortedDictionary<string, RenderedKey>(StringComparer.Ordinal);
        foreach (var row in await store.GetAsync(product.Id, target.Environment, ct))
            if (IsKey(row.Key) && !IsDerived(row.Key))
            {
                values[row.Key] = row.Value;
                keys[row.Key] = new RenderedKey("setting", null, SecretsVaultCrypto.Digest(row.Value));
            }
        var held = await secrets.ListAsync(target, product, ct);
        if (held is null)
            return AppResult<RenderValues>.Fail(AppErrorFactory.ExternalUnavailable(
                $"The vault is unreachable, so {target.Slug} cannot render its secrets; deploy once it answers."));
        var wanted = held.Keys.Where(key => IsKey(key) && !IsDerived(key)).ToList();
        if (wanted.Count > 0)
        {
            var read = await secrets.ReadAsync(target, product, wanted, "deploy " + target.Slug, ct);
            if (read is not AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Success { Data: var found })
            {
                var error = ((AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Failure)read).Error;
                return AppResult<RenderValues>.Fail(AppErrorFactory.ExternalUnavailable(
                    error.Message + " " + target.Slug + " waits: it renders its secrets at every deploy."));
            }
            foreach (var (key, secret) in found)
            {
                values[key] = secret.Value;
                keys[key] = new RenderedKey("secret", secret.Version, secret.Fingerprint);
            }
        }
        return AppResult<RenderValues>.Ok(new RenderValues(JsonSerializer.SerializeToElement(new { values }), keys));
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
        if (await secrets.ListAsync(target, product, ct) is not { } held)
            return null;
        keys.UnionWith(held.Keys);
        return [.. keys.Where(IsKey).Order(StringComparer.Ordinal)];
    }

    /// <summary>Records what a submitted deploy rendered, and which keys changed against the render of the target's last
    /// successful deploy (else its last render).</summary>
    /// <param name="target">The target's slug.</param>
    /// <param name="job">The runner's job identifier.</param>
    /// <param name="bundle">The release bundle.</param>
    /// <param name="rendered">What each key was.</param>
    /// <param name="at">When it was submitted.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The changes; null without an earlier render to compare.</returns>
    public async Task<IReadOnlyList<SettingChangeDto>?> RecordAsync(
        string target, Guid job, string? bundle, IReadOnlyDictionary<string, RenderedKey> rendered, DateTimeOffset at, CancellationToken ct)
    {
        var baseline = await BaselineAsync(target, ct);
        var changes = baseline is null ? null : Compare(Read(baseline.Keys), rendered);
        await renders.AddAsync(new DeploymentRenderEntity
        {
            JobId = job,
            Target = target,
            BundleId = bundle,
            Keys = JsonSerializer.Serialize(rendered, JsonSerializerOptions.Web),
            Changes = changes is null ? null : JsonSerializer.Serialize(changes, JsonSerializerOptions.Web),
            At = at,
        }, ct);
        return changes;
    }

    /// <summary>The keys that changed between two renders, by key: added, changed (a new digest, or a secret's new
    /// version) and removed.</summary>
    /// <param name="before">The earlier render.</param>
    /// <param name="after">The later one.</param>
    /// <returns>The changes in key order.</returns>
    public static IReadOnlyList<SettingChangeDto> Compare(
        IReadOnlyDictionary<string, RenderedKey> before, IReadOnlyDictionary<string, RenderedKey> after)
    {
        var changes = new List<SettingChangeDto>();
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var old = before.GetValueOrDefault(key);
            var now = after.GetValueOrDefault(key);
            if (old is null && now is not null)
                changes.Add(new SettingChangeDto(key, now.Kind, "added", null, now.Version));
            else if (old is not null && now is null)
                changes.Add(new SettingChangeDto(key, old.Kind, "removed", old.Version, null));
            else if (old is not null && now is not null
                     && (old.Kind != now.Kind || old.Fingerprint != now.Fingerprint || old.Version != now.Version))
                changes.Add(new SettingChangeDto(key, now.Kind, "changed", old.Version, now.Version));
        }
        return changes;
    }

    /// <summary>The release bundle a target runs: its latest successful deploy's; null when it never ran one.</summary>
    /// <param name="targetSlug">The target's slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The bundle and its label, or null.</returns>
    public async Task<(string Bundle, string? Release)?> RunningAsync(string targetSlug, CancellationToken ct)
    {
        var record = (await HistoryAsync(ct) ?? []).FirstOrDefault(item =>
            item.TargetId == targetSlug && item.Status == "succeeded" && item.BundleId is not null);
        return record is null ? null : (record.BundleId!, record.Release);
    }

    // ---- Planning ----

    private static readonly JsonElement Empty = JsonDocument.Parse("""{"services":[]}""").RootElement.Clone();

    /// <summary>Every submission the runner recorded, newest first; null when it cannot be read. @internal</summary>
    private async Task<IReadOnlyList<History.Models.DeploymentRecord>?> HistoryAsync(CancellationToken ct) =>
        await records.ListAsync(ct) is AppResult<IReadOnlyList<History.Models.DeploymentRecord>>.Success { Data: var list } ? list : null;

    private static string? CurrentBundle(IReadOnlyList<History.Models.DeploymentRecord> history, string target) =>
        history.FirstOrDefault(record => record.TargetId == target && record.Status == "succeeded" && record.BundleId is not null)?.BundleId;

    private static string? Label(IReadOnlyList<History.Models.DeploymentRecord> history, string bundle) =>
        history.FirstOrDefault(record => record.BundleId == bundle)?.Release;

    /// <summary>The render a new one compares against: the target's last successful deploy's, else its last. @internal</summary>
    private async Task<DeploymentRenderEntity?> BaselineAsync(string target, CancellationToken ct)
    {
        foreach (var record in (await HistoryAsync(ct) ?? []).Where(item => item.TargetId == target && item.Status == "succeeded"))
            if (Guid.TryParse(record.Id, out var id) && await renders.GetAsync(id, ct) is { } render)
                return render;
        return await renders.GetLatestAsync(target, ct);
    }

    /// <summary>The keys whose stored value or secret version differs from the target's last render. @internal</summary>
    private async Task<IReadOnlyList<string>> UnappliedAsync(
        string target, IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, SecretMetadataDto>? held, CancellationToken ct)
    {
        if (held is null || await renders.GetLatestAsync(target, ct) is not { } last)
            return [];
        var now = new Dictionary<string, RenderedKey>(StringComparer.Ordinal);
        foreach (var (key, value) in stored)
            if (IsKey(key) && !IsDerived(key))
                now[key] = new RenderedKey("setting", null, SecretsVaultCrypto.Digest(value));
        foreach (var (key, secret) in held)
            if (IsKey(key) && !IsDerived(key))
                now[key] = new RenderedKey("secret", secret.Version, string.IsNullOrEmpty(secret.Fingerprint) ? null : secret.Fingerprint);
        var before = Read(last.Keys);
        // A standalone vault keeps no fingerprint here: its versions alone tell a change.
        var comparable = before.ToDictionary(pair => pair.Key,
            pair => now.GetValueOrDefault(pair.Key) is { Kind: "secret", Fingerprint: null } ? pair.Value with { Fingerprint = null } : pair.Value,
            StringComparer.Ordinal);
        return [.. Compare(comparable, now).Select(change => change.Key)];
    }

    private static IReadOnlyDictionary<string, RenderedKey> Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, RenderedKey>>(json, JsonSerializerOptions.Web)
                   ?? new Dictionary<string, RenderedKey>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, RenderedKey>();
        }
    }

    /// <summary>Reads the runner's declarations into services, each key with its source. @internal</summary>
    private static List<DeploySettingsServiceDto> Services(
        JsonElement declared, IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, SecretMetadataDto> held,
        IReadOnlyList<ProductSettingKeyEntity> adHoc)
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
                var declaration = Declare(item, key);
                IReadOnlyList<string>? allowed = declaration.Choices;
                // A derived database string fills only a key no layer sets; every other derived key wins over the layers.
                var (source, value) = derived is not null && (derived != "database" || (!held.ContainsKey(key) && !stored.ContainsKey(key)))
                        ? (SettingSources.Derived, Text(item, "value"))
                    : held.ContainsKey(key) ? (SettingSources.Vault, null)
                    : stored.TryGetValue(key, out var kept) ? (SettingSources.Stored, secret ? null : kept)
                    : fallback is not null ? (SettingSources.Default, fallback)
                    : required ? (hostFile ? SettingSources.Host : SettingSources.Missing, null)
                    : (SettingSources.Unset, (string?)null);
                keys.Add(new DeploySettingDto(key, required, secret, source, value, fallback, derived, allowed)
                {
                    Type = SettingTypes.NameOf(declaration.Type),
                    Format = declaration.Format,
                    LooksSecret = derived is null && !declaration.Plain && SettingTypes.LooksSecret(key),
                    Vault = held.GetValueOrDefault(key),
                });
            }
            result.Add(new DeploySettingsServiceDto(name, hostFile, keys));
        }
        return result;
    }

    /// <summary>The keys no service declares: those the owner added, then stored values and vault secrets for keys no
    /// release names. @internal</summary>
    private static List<DeploySettingDto> Extra(
        IReadOnlySet<string> named, IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, SecretMetadataDto> held,
        IReadOnlyList<ProductSettingKeyEntity> adHoc)
    {
        var result = new List<DeploySettingDto>();
        var seen = new HashSet<string>(named, StringComparer.Ordinal);
        foreach (var key in adHoc.Where(key => seen.Add(key.Key)))
        {
            var declaration = Declare(key);
            var (source, value) = held.ContainsKey(key.Key) ? (SettingSources.Vault, null)
                : stored.TryGetValue(key.Key, out var kept) ? (SettingSources.Stored, key.Secret ? null : kept)
                : key.Required ? (SettingSources.Missing, (string?)null)
                : (SettingSources.Unset, null);
            result.Add(new DeploySettingDto(key.Key, key.Required, key.Secret, source, value, null, null, declaration.Choices)
            {
                Type = SettingTypes.NameOf(key.Type),
                Format = key.Format,
                AdHoc = true,
                LooksSecret = !key.Secret && SettingTypes.LooksSecret(key.Key),
                Vault = held.GetValueOrDefault(key.Key),
            });
        }
        foreach (var key in stored.Keys.Concat(held.Keys).Where(IsKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!seen.Add(key))
                continue;
            var secret = held.ContainsKey(key);
            result.Add(new DeploySettingDto(key, false, secret, secret ? SettingSources.Vault : SettingSources.Stored,
                secret ? null : stored[key])
            {
                LooksSecret = !secret && SettingTypes.LooksSecret(key),
                Vault = held.GetValueOrDefault(key),
            });
        }
        return result;
    }

    /// <summary>A runner key item's declaration. @internal</summary>
    private static KeyDeclaration Declare(JsonElement item, string key)
    {
        var type = SettingTypes.Parse(Text(item, "type")) ?? SettingType.Text;
        IReadOnlyList<string>? choices = item.TryGetProperty("allowed", out var values) && values.ValueKind == JsonValueKind.Array
            ? [.. values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!)]
            : null;
        if (choices is { Count: > 0 } && type == SettingType.Text)
            type = SettingType.Choice;
        var secret = item.TryGetProperty("secret", out var kind) && kind.ValueKind == JsonValueKind.True;
        var plain = !secret && (Text(item, "type") is not null || Text(item, "default") is not null);
        return new KeyDeclaration(key, type, Text(item, "format") ?? (type == SettingType.Date ? SettingTypes.DefaultDateFormat : null),
            choices, secret, plain, false);
    }

    /// <summary>An owner-added key's declaration. @internal</summary>
    private static KeyDeclaration Declare(ProductSettingKeyEntity key) =>
        new(key.Key, key.Type, key.Format ?? (key.Type == SettingType.Date ? SettingTypes.DefaultDateFormat : null),
            key.Choices is { Length: > 0 } choices ? [.. choices.Split('\n', StringSplitOptions.RemoveEmptyEntries)] : null,
            key.Secret, !key.Secret, true);

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
