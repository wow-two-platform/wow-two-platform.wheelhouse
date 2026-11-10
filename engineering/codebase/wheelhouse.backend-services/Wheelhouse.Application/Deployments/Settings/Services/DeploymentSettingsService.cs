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
/// takes each value from: what it derives, the environment's secrets, Wheelhouse's settings store, an owner's link to
/// another product's key in the same environment, a release default or the target's host file. Builds the values a
/// deploy sends the target, records what each render held, and the keys a check weighs.</summary>
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
    ISettingsApplies applies,
    ISettingLinksRepository links)
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
        var extra = Extra(named, stored, held ?? new Dictionary<string, SecretMetadataDto>(), adHoc, services.Count > 0);
        // The owner's links first: a release's from that reads a linked key sees it set by its source.
        var cross = await LinksAsync(target, product, ct);
        (services, extra) = Cross(services, extra, cross);
        services = Link(services, extra, stored, held ?? new Dictionary<string, SecretMetadataDto>());
        services = await UnreadAsync(services, product, target, ct);
        var pending = await applies.GetAsync(target.Id, ct);
        var label = bundle is null ? null : Text(declared, "release") ?? release ?? Label(history, bundle);
        return AppResult<DeploySettingsDto>.Ok(new DeploySettingsDto(
            targetSlug, product.Slug, EnvironmentName(target.Environment), label,
            read is not null && !history.Any(record => record.TargetId == targetSlug && record.Status == "succeeded"),
            place is null ? null : place with { Reachable = place.Reachable && held is not null },
            services,
            services.Where(service => !service.HostFile).Sum(service => service.Keys.Count(key => key.Source == SettingSources.Missing))
            + extra.Count(key => key.Source == SettingSources.Missing && !key.Held))
        {
            Bundle = bundle,
            Extra = extra,
            Unapplied = await UnappliedAsync(targetSlug, stored, held, cross,
                extra.Where(key => key.Held).Select(key => key.Key).ToHashSet(StringComparer.Ordinal), ct),
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
        foreach (var service in services.EnumerateArray())
        {
            var name = Text(service, "name");
            foreach (var item in service.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array
                         ? keys.EnumerateArray() : default(JsonElement.ArrayEnumerator))
            {
                if (Text(item, "key") is not { } key || Text(item, "derived") is not null)
                    continue;
                var declaration = Declare(item, key) with { Service = name };
                // A key one service declares as its own and another links stays the owner's to set: the link reads it.
                if (result.GetValueOrDefault(key) is { AdHoc: false, From: null } && declaration.From is not null)
                    continue;
                // The first service declaring how its value is made keeps it: its image runs a command.
                if (result.GetValueOrDefault(key) is { AdHoc: false, Generate: not null } earlier)
                    declaration = declaration with { Generate = earlier.Generate, Service = earlier.Service };
                result[key] = declaration;
            }
        }
        return result;
    }

    /// <summary>The values a deploy sends its target: the environment's stored settings, then its vault secrets over
    /// them, then each key the owner linked to another product's key with that source's current value, with what each key
    /// was. Refused while its secrets or a linked source's cannot be read (a sealed or unreachable vault), so a render
    /// never drops a secret a service had.</summary>
    /// <param name="target">The target.</param>
    /// <param name="product">The target's product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The values and their fingerprints, or why they could not be read.</returns>
    public Task<AppResult<RenderValues>> ValuesAsync(TargetEntity target, ProductEntity product, CancellationToken ct) =>
        ValuesAsync(target, product, null, ct);

    /// <summary>The values a deploy sends its target, as <see cref="ValuesAsync(TargetEntity, ProductEntity, CancellationToken)"/>,
    /// leaving out <paramref name="linked"/>: a linked key takes its source's value in the render, never one of its own.</summary>
    /// <param name="target">The target.</param>
    /// <param name="product">The target's product.</param>
    /// <param name="linked">The release's linked keys; null for none.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The values and their fingerprints, or why they could not be read.</returns>
    public Task<AppResult<RenderValues>> ValuesAsync(
        TargetEntity target, ProductEntity product, IReadOnlySet<string>? linked, CancellationToken ct) =>
        ValuesAsync(target, product, linked, null, ct);

    /// <summary>The values a deploy sends its target, as <see cref="ValuesAsync(TargetEntity, ProductEntity, IReadOnlySet{string}, CancellationToken)"/>,
    /// also leaving out <paramref name="withheld"/>: keys no service of the release declares, which wait for a release that
    /// does (the plan's <see cref="DeploySettingDto.Held"/>). A held key is neither sent nor recorded as rendered.</summary>
    /// <param name="target">The target.</param>
    /// <param name="product">The target's product.</param>
    /// <param name="linked">The release's linked keys; null for none.</param>
    /// <param name="withheld">The keys the render holds from every service; null for none.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The values and their fingerprints, or why they could not be read.</returns>
    public async Task<AppResult<RenderValues>> ValuesAsync(
        TargetEntity target, ProductEntity product, IReadOnlySet<string>? linked, IReadOnlySet<string>? withheld, CancellationToken ct)
    {
        if (withheld is { Count: > 0 })
            linked = linked is null ? withheld : new HashSet<string>(linked.Concat(withheld), StringComparer.Ordinal);
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var keys = new SortedDictionary<string, RenderedKey>(StringComparer.Ordinal);
        foreach (var row in await store.GetAsync(product.Id, target.Environment, ct))
            if (IsKey(row.Key) && !IsDerived(row.Key) && linked?.Contains(row.Key) != true)
            {
                values[row.Key] = row.Value;
                keys[row.Key] = new RenderedKey("setting", null, SecretsVaultCrypto.Digest(row.Value));
            }
        var held = await secrets.ListAsync(target, product, ct);
        if (held is null)
            return AppResult<RenderValues>.Fail(AppErrorFactory.ExternalUnavailable(
                $"The vault is unreachable, so {target.Slug} cannot render its secrets; deploy once it answers."));
        var wanted = held.Keys.Where(key => IsKey(key) && !IsDerived(key) && linked?.Contains(key) != true).ToList();
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
        if (await LinkedValuesAsync(target, product, linked, values, keys, ct) is { } refused)
            return AppResult<RenderValues>.Fail(refused);
        return AppResult<RenderValues>.Ok(new RenderValues(JsonSerializer.SerializeToElement(new { values }), keys));
    }

    /// <summary>Puts each linked key's source value under it, over the key's own: a source's vault secret (read through
    /// the source target's vault), else its stored setting; a source without a value leaves the key out. The error when
    /// a source's vault cannot say or read what it holds; null once every link is rendered. @internal</summary>
    private async Task<AppError?> LinkedValuesAsync(
        TargetEntity target, ProductEntity product, IReadOnlySet<string>? linked,
        SortedDictionary<string, string> values, SortedDictionary<string, RenderedKey> keys, CancellationToken ct)
    {
        var cross = (await LinksAsync(target, product, ct))
            .Where(item => IsKey(item.Link.Key) && !IsDerived(item.Link.Key) && linked?.Contains(item.Link.Key) != true).ToList();
        foreach (var item in cross)
        {
            values.Remove(item.Link.Key);
            keys.Remove(item.Link.Key);
        }
        foreach (var group in cross.GroupBy(item => item.Source.Id))
        {
            var source = group.First();
            if (!source.Known)
                return AppErrorFactory.ExternalUnavailable(
                    $"The vault of {source.Source.Slug} is unreachable, so {target.Slug} cannot render the keys it links there; deploy once it answers.");
            var wanted = group.Where(item => item.Secret is not null).Select(item => item.Link.SourceKey).Distinct(StringComparer.Ordinal).ToList();
            IReadOnlyDictionary<string, RenderedSecret> found = new Dictionary<string, RenderedSecret>(StringComparer.Ordinal);
            if (wanted.Count > 0)
            {
                var read = await secrets.ReadAsync(source.Source, source.Owner, wanted, "deploy " + target.Slug + " (link)", ct);
                if (read is not AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Success { Data: var data })
                {
                    var error = ((AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Failure)read).Error;
                    return AppErrorFactory.ExternalUnavailable(
                        error.Message + " " + target.Slug + " waits: it renders the keys it links to " + source.Source.Slug + " at every deploy.");
                }
                found = data;
            }
            foreach (var item in group)
            {
                if (found.TryGetValue(item.Link.SourceKey, out var secret))
                {
                    values[item.Link.Key] = secret.Value;
                    keys[item.Link.Key] = new RenderedKey("secret", secret.Version, secret.Fingerprint);
                }
                else if (item.Secret is null && item.Stored is { } value)
                {
                    values[item.Link.Key] = value;
                    keys[item.Link.Key] = new RenderedKey("setting", null, SecretsVaultCrypto.Digest(value));
                }
            }
        }
        return null;
    }

    /// <summary>The setting keys Wheelhouse would send a target, without values, for its readiness check; null when the
    /// vault cannot say which secrets it holds.</summary>
    /// <param name="target">The target.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The keys, or null when unknown.</returns>
    public Task<IReadOnlyCollection<string>?> KeysAsync(TargetEntity target, CancellationToken ct) => KeysAsync(target, null, ct);

    /// <summary>The setting keys Wheelhouse would send a target for <paramref name="release"/>, without values, for its
    /// readiness check: stored settings and vault secrets, each key the owner linked to another product's key that holds a
    /// value, each key the release has Wheelhouse generate (a deploy makes it before it starts), and each linked key whose
    /// source is among them or derived. Null when a vault cannot say which secrets it holds.</summary>
    /// <param name="target">The target.</param>
    /// <param name="release">The release bundle; null for the one the target runs.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The keys, or null when unknown.</returns>
    public async Task<IReadOnlyCollection<string>?> KeysAsync(TargetEntity target, string? release, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(target.ProductId, ct);
        if (product is null)
            return null;
        var keys = (await store.GetAsync(product.Id, target.Environment, ct)).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        if (await secrets.ListAsync(target, product, ct) is not { } held)
            return null;
        keys.UnionWith(held.Keys);
        foreach (var item in await LinksAsync(target, product, ct))
        {
            if (!item.Known)
                return null;
            if (item.Secret is not null || item.Stored is not null)
                keys.Add(item.Link.Key);
            else
                keys.Remove(item.Link.Key);
        }
        var declared = await DeclarationsAsync(target, product, release, ct);
        keys.UnionWith(declared.Values.Where(key => key.Generate is not null && key.From is null).Select(key => key.Key));
        keys.UnionWith(declared.Values.Where(key => key.From is { } from && (keys.Contains(from) || IsDerived(from))).Select(key => key.Key));
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

    /// <summary>The keys whose stored value or secret version differs from the target's last render; a linked key
    /// compares its source's, so a source's new version marks it. A key the next deploy holds (<paramref name="withheld"/>)
    /// counts as absent: it waits for a release that declares it, so no deploy applies it; one the last render still
    /// held a value for reads as changed until a deploy drops it. @internal</summary>
    private async Task<IReadOnlyList<string>> UnappliedAsync(
        string target, IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, SecretMetadataDto>? held,
        IReadOnlyList<LinkedSource> cross, IReadOnlySet<string> withheld, CancellationToken ct)
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
        foreach (var item in cross.Where(item => item.Known && IsKey(item.Link.Key) && !IsDerived(item.Link.Key)))
        {
            now.Remove(item.Link.Key);
            if (item.Secret is { } secret)
                now[item.Link.Key] = new RenderedKey("secret", secret.Version, string.IsNullOrEmpty(secret.Fingerprint) ? null : secret.Fingerprint);
            else if (item.Stored is { } value)
                now[item.Link.Key] = new RenderedKey("setting", null, SecretsVaultCrypto.Digest(value));
        }
        foreach (var key in withheld)
            now.Remove(key);
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
                var derived = Text(item, "derived");
                var fallback = Text(item, "default");
                var declaration = Declare(item, key);
                // A generated key is a secret whatever its name; a linked one takes its source's secrecy (Link).
                var secret = declaration.Secret || declaration.Generate is not null;
                IReadOnlyList<string>? allowed = declaration.Choices;
                // A derived database string fills only a key no layer sets; every other derived key wins over the layers.
                // A linked key holds no value of its own; a generated one waits for the next deploy to make it.
                var (source, value) = derived is not null && (derived != "database" || (!held.ContainsKey(key) && !stored.ContainsKey(key)))
                        ? (SettingSources.Derived, Text(item, "value"))
                    : declaration.From is not null ? (SettingSources.Linked, null)
                    : held.ContainsKey(key) ? (SettingSources.Vault, null)
                    : stored.TryGetValue(key, out var kept) ? (SettingSources.Stored, secret ? null : kept)
                    : declaration.Generate is not null ? (SettingSources.Generate, null)
                    : fallback is not null ? (SettingSources.Default, fallback)
                    : required ? (hostFile ? SettingSources.Host : SettingSources.Missing, null)
                    : (SettingSources.Unset, (string?)null);
                keys.Add(new DeploySettingDto(key, required, secret, source, value, fallback, derived, allowed)
                {
                    Type = SettingTypes.NameOf(declaration.Type),
                    Format = declaration.Format,
                    LooksSecret = derived is null && declaration.From is null && !declaration.Plain && SettingTypes.LooksSecret(key),
                    Vault = declaration.From is null ? held.GetValueOrDefault(key) : null,
                    Generate = derived is null && declaration.From is null ? declaration.Generate : null,
                    From = derived is null ? declaration.From : null,
                });
            }
            result.Add(new DeploySettingsServiceDto(name, hostFile, keys));
        }
        return result;
    }

    /// <summary>Resolves each linked key against its source, in the same environment: it takes the source's type and
    /// secrecy, and a plain source's known value. It counts as set while its source is (a value, a default, a host file,
    /// a derived key or a key the next deploy generates); a required one whose source is not is missing, so a deploy
    /// waits for the source. @internal</summary>
    private static List<DeploySettingsServiceDto> Link(
        List<DeploySettingsServiceDto> services, IReadOnlyList<DeploySettingDto> extra,
        IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, SecretMetadataDto> held)
    {
        if (!services.Any(service => service.Keys.Any(key => key.From is not null)))
            return services;
        var own = new Dictionary<string, DeploySettingDto>(StringComparer.Ordinal);
        foreach (var key in services.SelectMany(service => service.Keys).Concat(extra).Where(key => key.From is null))
            if (!own.TryGetValue(key.Key, out var known) || (!IsSet(known) && IsSet(key)))
                own[key.Key] = key;
        return
        [
            .. services.Select(service => service with
            {
                Keys =
                [
                    .. service.Keys.Select(key =>
                    {
                        if (key.From is not { } from)
                            return key;
                        var origin = own.GetValueOrDefault(from);
                        var set = origin is not null ? IsSet(origin)
                            : IsDerived(from) || stored.ContainsKey(from) || held.ContainsKey(from);
                        var secret = origin?.Secret ?? held.ContainsKey(from);
                        return key with
                        {
                            Secret = secret,
                            Source = set || !key.Required ? SettingSources.Linked
                                : service.HostFile ? SettingSources.Host : SettingSources.Missing,
                            Value = !secret && origin is { Source: SettingSources.Stored or SettingSources.Default or SettingSources.Derived or SettingSources.Linked }
                                ? origin.Value : null,
                            Type = origin?.Type ?? key.Type,
                            Format = origin?.Format ?? key.Format,
                            Allowed = origin?.Allowed ?? key.Allowed,
                        };
                    }),
                ],
            }),
        ];
    }

    /// <summary>The owner's links of a target's product environment, each with its source resolved: the source's target
    /// and product, what that environment's vault holds for the key and its stored value. A link whose source target is
    /// gone is passed over. @internal</summary>
    private async Task<IReadOnlyList<LinkedSource>> LinksAsync(TargetEntity target, ProductEntity product, CancellationToken ct)
    {
        var result = new List<LinkedSource>();
        foreach (var group in (await links.GetAsync(product.Id, target.Environment, ct)).GroupBy(link => link.SourceTargetId))
        {
            if (await targets.GetByIdAsync(group.Key, ct) is not { } source || await products.GetByIdAsync(source.ProductId, ct) is not { } owner)
                continue;
            var stored = (await store.GetAsync(owner.Id, source.Environment, ct)).ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
            var held = await secrets.ListAsync(source, owner, ct);
            result.AddRange(group.Select(link => new LinkedSource(
                link, source, owner, held is not null, held?.GetValueOrDefault(link.SourceKey), stored.GetValueOrDefault(link.SourceKey))));
        }
        return result;
    }

    /// <summary>Marks each key the owner linked to another product's key: it takes the source's secrecy, vault metadata
    /// and a plain source's value, and counts as set while the source holds a value (or its vault cannot say); a required
    /// one whose source holds none is missing. A link on a key no release declares joins the extra keys. A key the
    /// render derives, or the release links with <c>from</c>, keeps that. @internal</summary>
    private static (List<DeploySettingsServiceDto> Services, List<DeploySettingDto> Extra) Cross(
        List<DeploySettingsServiceDto> services, List<DeploySettingDto> extra, IReadOnlyList<LinkedSource> cross)
    {
        if (cross.Count == 0)
            return (services, extra);
        var byKey = cross.ToDictionary(item => item.Link.Key, StringComparer.Ordinal);
        services =
        [
            .. services.Select(service => service with
            {
                Keys =
                [
                    .. service.Keys.Select(key => key.Derived is null && key.From is null && byKey.TryGetValue(key.Key, out var item)
                        ? Linked(key, item, service.HostFile) : key),
                ],
            }),
        ];
        extra = [.. extra.Select(key => byKey.TryGetValue(key.Key, out var item) ? Linked(key, item, false) : key)];
        var listed = services.SelectMany(service => service.Keys).Concat(extra).Select(key => key.Key).ToHashSet(StringComparer.Ordinal);
        extra.AddRange(cross.Where(item => !listed.Contains(item.Link.Key) && IsKey(item.Link.Key) && !IsDerived(item.Link.Key))
            .OrderBy(item => item.Link.Key, StringComparer.Ordinal)
            .Select(item => Linked(new DeploySettingDto(item.Link.Key, false, false, SettingSources.Linked), item, false)));
        return (services, extra);
    }

    /// <summary>A key as its link to another product's key makes it; never a value of a secret. @internal</summary>
    private static DeploySettingDto Linked(DeploySettingDto key, LinkedSource item, bool hostFile)
    {
        var set = !item.Known || item.Secret is not null || item.Stored is not null;
        return key with
        {
            Secret = item.Secret is not null || (item.Stored is null && key.Secret),
            Source = set || !key.Required ? SettingSources.Linked : hostFile ? SettingSources.Host : SettingSources.Missing,
            Value = item.Secret is null ? item.Stored : null,
            Vault = item.Secret,
            Generate = null,
            LooksSecret = false,
            Link = new SettingLinkDto(item.Source.Slug, item.Link.SourceKey),
        };
    }

    /// <summary>An owner's link with its source resolved; <see cref="Known"/> is false while the source's vault cannot
    /// say what it holds. @internal</summary>
    private sealed record LinkedSource(
        SettingLinkEntity Link, TargetEntity Source, ProductEntity Owner, bool Known, SecretMetadataDto? Secret, string? Stored);

    /// <summary>Marks each client secret Wheelhouse generated that nothing reads (<see cref="DeploySettingDto.Unread"/>): a
    /// key named <c>…ClientSecret</c> the release has Wheelhouse generate and the vault holds, which no key of the release
    /// takes with <c>from</c> and no owner link from another product's target reads. Only a client secret: a generated
    /// key its own service uses (a signing key, a hash key) has no reader by design. @internal</summary>
    private async Task<List<DeploySettingsServiceDto>> UnreadAsync(
        List<DeploySettingsServiceDto> services, ProductEntity product, TargetEntity target, CancellationToken ct)
    {
        var read = services.SelectMany(service => service.Keys).Select(key => key.From).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var candidates = services.SelectMany(service => service.Keys)
            .Where(key => key.Key.EndsWith("ClientSecret", StringComparison.Ordinal) && key.Generate is not null
                && key.From is null && key.Link is null && key.Vault is not null && !read.Contains(key.Key))
            .Select(key => key.Key).Distinct(StringComparer.Ordinal).ToList();
        if (candidates.Count == 0)
            return services;
        var unread = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in candidates)
            if ((await links.GetBySourceAsync(product.Id, target.Environment, key, ct)).Count == 0)
                unread.Add(key);
        return unread.Count == 0 ? services : services.Select(service => service with
        {
            Keys = service.Keys.Select(key => unread.Contains(key.Key) ? key with { Unread = true } : key).ToList(),
        }).ToList();
    }

    /// <summary>Whether a key holds a value, or will by the time a deploy renders it. @internal</summary>
    private static bool IsSet(DeploySettingDto key) => key.Source is not (SettingSources.Missing or SettingSources.Unset);

    /// <summary>The keys no service declares: those the owner added, then stored values and vault secrets for keys no
    /// release names. Once the release declares any service (<paramref name="declares"/>), each is held from every service
    /// unless the owner added it for every service: a key typed ahead of its release waits for the release that declares
    /// it. A release that declares none (an unreadable one, or none) holds nothing. @internal</summary>
    private static List<DeploySettingDto> Extra(
        IReadOnlySet<string> named, IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, SecretMetadataDto> held,
        IReadOnlyList<ProductSettingKeyEntity> adHoc, bool declares)
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
                EveryService = key.EveryService,
                Held = declares && !key.EveryService,
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
                Held = declares,
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
            choices, secret, plain, false)
        {
            Generate = Generation(item),
            From = Text(item, "from") is { } from && IsKey(from) ? from : null,
        };
    }

    /// <summary>Reads a key item's <c>generate</c>: <c>{random: N, encoding}</c> or <c>{command: [argv], timeoutSeconds}</c>;
    /// null when it declares none or one Wheelhouse cannot read. @internal</summary>
    private static SettingGenerateDto? Generation(JsonElement item)
    {
        if (!item.TryGetProperty("generate", out var generate) || generate.ValueKind != JsonValueKind.Object)
            return null;
        if (generate.TryGetProperty("command", out var argv) && argv.ValueKind == JsonValueKind.Array)
        {
            IReadOnlyList<string> command = [.. argv.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.String).Select(part => part.GetString()!)];
            int? timeout = generate.TryGetProperty("timeoutSeconds", out var seconds) && seconds.ValueKind == JsonValueKind.Number
                && seconds.TryGetInt32(out var limit) ? limit : null;
            return command.Count > 0 ? new SettingGenerateDto(Command: command, TimeoutSeconds: timeout) : null;
        }
        return generate.TryGetProperty("random", out var random) && random.ValueKind == JsonValueKind.Number && random.TryGetInt32(out var bytes)
            ? new SettingGenerateDto(bytes, Text(generate, "encoding") ?? SecretValues.Base64Url)
            : null;
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
