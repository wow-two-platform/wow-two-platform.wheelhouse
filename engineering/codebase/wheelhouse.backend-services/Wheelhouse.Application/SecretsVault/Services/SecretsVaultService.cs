using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.Constants;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Domain.SecretsVault.Entities;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Services;

/// <summary>The built-in Secrets Vault: unseals with the root key file, keeps each product's data key wrapped by it,
/// writes every change of a secret as a new encrypted version and reads current values for a deploy's render only.</summary>
/// <remarks>
/// Values never leave this service except to a render's caller, which sends them to the runner's stdin; nothing here
/// logs a value, a key or a fingerprint. Metadata — key names, versions, who, when, expiry — reads while sealed; values
/// neither read nor write. Writes are staged on the store, so the caller's transaction commits them with the settings.
/// </remarks>
public sealed partial class SecretsVaultService(
    ISecretsVaultStore store,
    IMasterKeyBroker broker,
    MasterKeyProofTracker proofs,
    MasterKeyUnsealService unseal,
    IValueCipher cipher,
    TimeProvider time,
    ILogger<SecretsVaultService> logger)
{
    /// <summary>How long before a secret lapses the first warning shows.</summary>
    public static readonly TimeSpan FirstWarning = TimeSpan.FromDays(30);

    /// <summary>How long before a secret lapses the second warning shows.</summary>
    public static readonly TimeSpan SecondWarning = TimeSpan.FromDays(7);

    /// <summary>Where the root key is read from.</summary>
    public string KeyFile => broker.Location;

    /// <summary>How close an expiry is at <paramref name="now"/>: <c>expired</c>, <c>7d</c>, <c>30d</c> or null.</summary>
    /// <param name="expiresAt">The expiry, if any.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The level.</returns>
    public static string? ExpiryLevel(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is not { } at ? null
        : at <= now ? "expired"
        : at - now <= SecondWarning ? "7d"
        : at - now <= FirstWarning ? "30d"
        : null;

    /// <summary>Reads the root key and proves it against the canary (or, before the first canary, a product key), once
    /// per key file content, through the SDK <see cref="MasterKeyUnsealService"/>.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The root key, which the caller clears after use, or why the vault is sealed.</returns>
    public async Task<(byte[]? Root, string? Reason)> UnsealAsync(CancellationToken ct)
    {
        var read = await unseal.UnsealAsync(ProveAsync, ct);
        if (read.Key is { } root)
            return (root, null);
        if (read.Problem == MasterKeyRead.Mismatch)
            LogMismatch(logger);
        return (null, read.Problem ?? SecretsVaultStates.Missing);
    }

    /// <summary>Reads the vault's state and what it holds; never a value.</summary>
    /// <param name="mode">Where deploys take secrets from.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The state.</returns>
    public async Task<VaultStatusDto> StatusAsync(string mode, CancellationToken ct)
    {
        var (root, reason) = await UnsealAsync(ct);
        if (root is not null)
            CryptographicOperations.ZeroMemory(root);
        var now = time.GetUtcNow();
        var secrets = (await store.GetAllSecretsAsync(ct)).Where(secret => secret.RemovedAt is null).ToList();
        var levels = secrets.Select(secret => ExpiryLevel(secret.ExpiresAt, now)).ToList();
        return new VaultStatusDto(mode, reason is null ? SecretsVaultStates.Unsealed : SecretsVaultStates.Sealed, reason, KeyFile,
            (await store.GetProductKeysAsync(ct)).Count, secrets.Count,
            levels.Count(level => level is "7d" or "30d"), levels.Count(level => level == "expired"));
    }

    /// <summary>An environment's secrets, removed ones left out, with their current version's metadata.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The metadata by key.</returns>
    public async Task<IReadOnlyDictionary<string, SecretMetadataDto>> ListAsync(
        Guid productId, DeploymentEnvironment environment, CancellationToken ct)
    {
        var secrets = (await store.GetSecretsAsync(productId, environment, ct)).Where(secret => secret.RemovedAt is null).ToList();
        var versions = await store.GetCurrentVersionsAsync(secrets, ct);
        var now = time.GetUtcNow();
        var result = new SortedDictionary<string, SecretMetadataDto>(StringComparer.Ordinal);
        foreach (var secret in secrets)
        {
            if (!versions.TryGetValue(secret.Id, out var version))
                continue;
            result[secret.Key] = new SecretMetadataDto(secret.Key, version.Version, version.CreatedAt, version.CreatedBy,
                version.Fingerprint, secret.ExpiresAt, ExpiryLevel(secret.ExpiresAt, now));
        }
        return result;
    }

    /// <summary>Reads the current values of an environment's secrets for a deploy's render. Refused while sealed.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="keys">The keys to read; one the vault lacks or removed is left out.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The values with their versions, or why they could not be read.</returns>
    public async Task<AppResult<IReadOnlyDictionary<string, RenderedSecret>>> ReadAsync(
        Guid productId, DeploymentEnvironment environment, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        var secrets = (await store.GetSecretsAsync(productId, environment, ct))
            .Where(secret => secret.RemovedAt is null && wanted.Contains(secret.Key)).ToList();
        if (secrets.Count == 0)
            return AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Ok(new Dictionary<string, RenderedSecret>());
        var (root, reason) = await UnsealAsync(ct);
        if (root is null)
            return AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Fail(Sealed(reason));
        byte[]? dataKey = null;
        try
        {
            var stored = await store.GetProductKeyAsync(productId, ct);
            if (stored is null)
                return AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Fail(
                    AppErrorFactory.Unexpected("The product's data key is missing; run the vault check."));
            dataKey = cipher.Decrypt(Payload(stored.WrappedKey, stored.Nonce, stored.Tag), root, SecretsVaultCrypto.ProductKeyContext(productId));
            var versions = await store.GetCurrentVersionsAsync(secrets, ct);
            var values = new SortedDictionary<string, RenderedSecret>(StringComparer.Ordinal);
            foreach (var secret in secrets)
            {
                if (!versions.TryGetValue(secret.Id, out var version))
                    continue;
                values[secret.Key] = new RenderedSecret(
                    cipher.DecryptText(Payload(version.CipherText, version.Nonce, version.Tag), dataKey,
                        SecretsVaultCrypto.ValueContext(productId, environment, secret.Key, version.Version)),
                    version.Version, version.Fingerprint);
            }
            return AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Ok(values);
        }
        catch (CryptographicException)
        {
            LogUndecryptable(logger);
            return AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Fail(
                AppErrorFactory.Unexpected("A secret could not be decrypted; run the vault check."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
            if (dataKey is not null)
                CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>Stages a new version of each secret, creating the secret and the product's data key when missing, for
    /// the caller to save. Refused while sealed.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="writes">The values.</param>
    /// <param name="actor">Who writes them.</param>
    /// <param name="origin">Where the values came from when the owner did not type them.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each new version, or why nothing was staged.</returns>
    public async Task<AppResult<IReadOnlyList<SecretWritten>>> StageWritesAsync(
        Guid productId, DeploymentEnvironment environment, IReadOnlyList<SecretWrite> writes, string actor, string? origin,
        CancellationToken ct)
    {
        if (writes.Count == 0)
            return AppResult<IReadOnlyList<SecretWritten>>.Ok([]);
        var (root, reason) = await UnsealAsync(ct);
        if (root is null)
            return AppResult<IReadOnlyList<SecretWritten>>.Fail(Sealed(reason));
        byte[]? dataKey = null;
        try
        {
            await EnsureCanaryAsync(root, ct);
            dataKey = await DataKeyAsync(root, productId, ct);
            var written = new List<SecretWritten>(writes.Count);
            foreach (var write in writes)
                written.Add(await StageVersionAsync(productId, environment, write, dataKey, actor, origin, ct));
            return AppResult<IReadOnlyList<SecretWritten>>.Ok(written);
        }
        catch (CryptographicException)
        {
            LogUndecryptable(logger);
            return AppResult<IReadOnlyList<SecretWritten>>.Fail(
                AppErrorFactory.Unexpected("The product's data key could not be unwrapped; run the vault check."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
            if (dataKey is not null)
                CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>Stages a new version holding an earlier version's value, for the caller to save.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="key">The secret's key.</param>
    /// <param name="version">The version to restore.</param>
    /// <param name="note">The owner's note.</param>
    /// <param name="actor">Who restores it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The new version, or why it could not be made.</returns>
    public async Task<AppResult<SecretWritten>> StageRestoreAsync(
        Guid productId, DeploymentEnvironment environment, string key, int version, string? note, string actor, CancellationToken ct)
    {
        var secret = await store.GetSecretAsync(productId, environment, key, ct);
        var old = secret is null ? null : await store.GetVersionAsync(secret.Id, version, ct);
        if (secret is null || old is null)
            return AppResult<SecretWritten>.Fail(AppErrorFactory.NotFound($"{key} has no version {version} here."));
        if (version == secret.CurrentVersion && secret.RemovedAt is null)
            return AppResult<SecretWritten>.Fail(AppErrorFactory.Conflict($"{key} is already on version {version}."));
        var (root, reason) = await UnsealAsync(ct);
        if (root is null)
            return AppResult<SecretWritten>.Fail(Sealed(reason));
        byte[]? dataKey = null;
        try
        {
            dataKey = await DataKeyAsync(root, productId, ct);
            var value = cipher.DecryptText(Payload(old.CipherText, old.Nonce, old.Tag), dataKey,
                SecretsVaultCrypto.ValueContext(productId, environment, key, old.Version));
            return AppResult<SecretWritten>.Ok(await StageVersionAsync(productId, environment, new SecretWrite(key, value, note),
                dataKey, actor, "restored from v" + old.Version, ct));
        }
        catch (CryptographicException)
        {
            LogUndecryptable(logger);
            return AppResult<SecretWritten>.Fail(AppErrorFactory.Unexpected($"Version {version} of {key} could not be decrypted."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
            if (dataKey is not null)
                CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>Stages a secret's removal: it renders nowhere from then on, and its versions stay.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="key">The secret's key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The version it was on, or null when the environment held no such secret.</returns>
    public async Task<int?> StageRemoveAsync(Guid productId, DeploymentEnvironment environment, string key, CancellationToken ct)
    {
        var secret = await store.GetSecretAsync(productId, environment, key, ct);
        if (secret is null || secret.RemovedAt is not null)
            return null;
        secret.RemovedAt = time.GetUtcNow();
        return secret.CurrentVersion;
    }

    /// <summary>Stages a secret's expiry; null clears it.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="key">The secret's key.</param>
    /// <param name="expiresAt">When it lapses.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Whether the environment holds the secret.</returns>
    public async Task<bool> StageExpiryAsync(
        Guid productId, DeploymentEnvironment environment, string key, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        var secret = await store.GetSecretAsync(productId, environment, key, ct);
        if (secret is null || secret.RemovedAt is not null)
            return false;
        secret.ExpiresAt = expiresAt;
        return true;
    }

    /// <summary>A secret's history, newest version first; never a value.</summary>
    /// <param name="target">The target whose environment holds it.</param>
    /// <param name="productId">The product.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="key">The secret's key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The history, or null when the environment never held the secret.</returns>
    public async Task<SecretHistoryDto?> HistoryAsync(
        string target, Guid productId, DeploymentEnvironment environment, string key, CancellationToken ct)
    {
        var secret = await store.GetSecretAsync(productId, environment, key, ct);
        if (secret is null)
            return null;
        var versions = await store.GetVersionsAsync(secret.Id, ct);
        return new SecretHistoryDto(target, key, secret.RemovedAt is not null, secret.ExpiresAt, [.. versions.Select(version =>
            new SecretVersionDto(version.Version, version.CreatedBy, version.CreatedAt, version.Fingerprint, version.Note,
                version.Origin, version.Version == secret.CurrentVersion && secret.RemovedAt is null))]);
    }

    /// <summary>Decrypts everything the vault holds — the canary, each product key, every version of every secret — and
    /// counts what decrypted; names what did not, never a value.</summary>
    /// <param name="productSlugs">Product slugs by identifier, to name failures.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The counts.</returns>
    public async Task<VaultVerificationDto> VerifyAsync(IReadOnlyDictionary<Guid, string> productSlugs, CancellationToken ct)
    {
        proofs.Forget();
        var (root, _) = await UnsealAsync(ct);
        if (root is null)
            return new VaultVerificationDto(SecretsVaultStates.Sealed, false, 0, 0, 0, 0, []);
        var failures = new List<string>();
        var dataKeys = new Dictionary<Guid, byte[]>();
        try
        {
            var canary = await store.GetCanaryAsync(ct);
            var canaryOk = canary is not null && CheckCanary(canary, root);
            foreach (var stored in await store.GetProductKeysAsync(ct))
            {
                try
                {
                    dataKeys[stored.ProductId] = cipher.Decrypt(Payload(stored.WrappedKey, stored.Nonce, stored.Tag), root,
                        SecretsVaultCrypto.ProductKeyContext(stored.ProductId));
                }
                catch (CryptographicException)
                {
                    failures.Add(productSlugs.GetValueOrDefault(stored.ProductId, stored.ProductId.ToString()) + " data key");
                }
            }
            var secrets = (await store.GetAllSecretsAsync(ct)).ToDictionary(secret => secret.Id);
            var versions = 0;
            foreach (var version in await store.GetAllVersionsAsync(ct))
            {
                if (!secrets.TryGetValue(version.SecretId, out var secret))
                    continue;
                var name = productSlugs.GetValueOrDefault(secret.ProductId, secret.ProductId.ToString()) + "/"
                    + secret.Environment.ToString().ToLowerInvariant() + "/" + secret.Key + " v" + version.Version;
                if (!dataKeys.TryGetValue(secret.ProductId, out var dataKey))
                {
                    failures.Add(name);
                    continue;
                }
                try
                {
                    var value = cipher.DecryptText(Payload(version.CipherText, version.Nonce, version.Tag), dataKey,
                        SecretsVaultCrypto.ValueContext(secret.ProductId, secret.Environment, secret.Key, version.Version));
                    if (SecretsVaultCrypto.ValueFingerprint.Hash(value, dataKey) == version.Fingerprint)
                        versions++;
                    else
                        failures.Add(name);
                }
                catch (CryptographicException)
                {
                    failures.Add(name);
                }
            }
            return new VaultVerificationDto(SecretsVaultStates.Unsealed, canaryOk, dataKeys.Count, secrets.Count, versions,
                failures.Count, [.. failures.Take(50)]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
            foreach (var key in dataKeys.Values)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Every secret lapsing within 30 days or lapsed, removed ones left out, soonest first.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each secret with its level.</returns>
    public async Task<IReadOnlyList<(VaultSecretEntity Secret, string Level)>> ExpiriesAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return [.. (await store.GetAllSecretsAsync(ct))
            .Where(secret => secret.RemovedAt is null)
            .Select(secret => (Secret: secret, Level: ExpiryLevel(secret.ExpiresAt, now)))
            .Where(item => item.Level is not null)
            .OrderBy(item => item.Secret.ExpiresAt)
            .Select(item => (item.Secret, item.Level!))];
    }

    /// <summary>Stages imported secrets with their version numbers, authors and times, each value encrypted under its
    /// product's data key, then decrypts each staged version again and compares it with the value read. The caller saves
    /// only when nothing failed.</summary>
    /// <param name="secrets">The secrets; the caller left out those the vault already holds.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The versions staged and verified, and what failed by key; or why nothing was staged.</returns>
    public async Task<AppResult<(int Versions, int Verified, IReadOnlyList<string> Failures)>> StageImportAsync(
        IReadOnlyList<ImportedSecret> secrets, CancellationToken ct)
    {
        var (root, reason) = await UnsealAsync(ct);
        if (root is null)
            return AppResult<(int, int, IReadOnlyList<string>)>.Fail(Sealed(reason));
        var dataKeys = new Dictionary<Guid, byte[]>();
        try
        {
            await EnsureCanaryAsync(root, ct);
            int staged = 0, verified = 0;
            var failures = new List<string>();
            foreach (var imported in secrets)
            {
                if (!dataKeys.TryGetValue(imported.ProductId, out var dataKey))
                    dataKeys[imported.ProductId] = dataKey = await DataKeyAsync(root, imported.ProductId, ct);
                var secret = new VaultSecretEntity
                {
                    Id = Guid.CreateVersion7(), ProductId = imported.ProductId, Environment = imported.Environment,
                    Key = imported.Key, CurrentVersion = imported.CurrentVersion,
                    RemovedAt = imported.Disabled ? time.GetUtcNow() : null,
                };
                store.Add(secret);
                foreach (var version in imported.Versions)
                {
                    var context = SecretsVaultCrypto.ValueContext(imported.ProductId, imported.Environment, imported.Key, version.Version);
                    var sealedValue = cipher.EncryptText(version.Value, dataKey, context);
                    var fingerprint = SecretsVaultCrypto.ValueFingerprint.Hash(version.Value, dataKey);
                    store.Add(new VaultSecretVersionEntity
                    {
                        Id = Guid.CreateVersion7(), SecretId = secret.Id, Version = version.Version,
                        CipherText = sealedValue.CipherText, Nonce = sealedValue.Nonce, Tag = sealedValue.Tag,
                        Fingerprint = fingerprint, Origin = imported.Origin, CreatedBy = version.CreatedBy,
                        CreatedAt = version.CreatedAt,
                    });
                    staged++;
                    try
                    {
                        if (cipher.DecryptText(sealedValue, dataKey, context) == version.Value)
                            verified++;
                        else
                            failures.Add(imported.Key + " v" + version.Version);
                    }
                    catch (CryptographicException)
                    {
                        failures.Add(imported.Key + " v" + version.Version);
                    }
                }
            }
            return AppResult<(int, int, IReadOnlyList<string>)>.Ok((staged, verified, failures));
        }
        catch (CryptographicException)
        {
            LogUndecryptable(logger);
            return AppResult<(int, int, IReadOnlyList<string>)>.Fail(
                AppErrorFactory.Unexpected("A product's data key could not be unwrapped; run the vault check."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
            foreach (var key in dataKeys.Values)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    // ---- Keys ----

    /// <summary>Whether the root key opens the canary, or with none yet, the first product key; an empty vault takes
    /// any key until its first write binds it.</summary>
    private async Task<bool> ProveAsync(byte[] root, CancellationToken ct)
    {
        var canary = await store.GetCanaryAsync(ct);
        if (canary is not null)
            return CheckCanary(canary, root);
        var keys = await store.GetProductKeysAsync(ct);
        if (keys.Count == 0)
            return true;
        var first = keys[0];
        var opened = cipher.Decrypt(Payload(first.WrappedKey, first.Nonce, first.Tag), root, SecretsVaultCrypto.ProductKeyContext(first.ProductId));
        CryptographicOperations.ZeroMemory(opened);
        return true;
    }

    /// <summary>Whether the canary decrypts under the root key to its digest.</summary>
    private bool CheckCanary(VaultCanaryEntity canary, byte[] root) =>
        cipher.VerifyCanary(
            new KeyCanary
            {
                WrappedKey = Payload(canary.WrappedKey, canary.KeyNonce, canary.KeyTag),
                Value = Payload(canary.CipherText, canary.Nonce, canary.Tag),
                Digest = canary.Digest,
            },
            root,
            SecretsVaultCrypto.CanaryKeyContext,
            SecretsVaultCrypto.CanaryContext);

    /// <summary>Makes the canary on the vault's first write, so a later root key is proven against it.</summary>
    private async Task EnsureCanaryAsync(byte[] root, CancellationToken ct)
    {
        if (await store.GetCanaryAsync(ct) is not null)
            return;
        var canary = cipher.CreateCanary(root, SecretsVaultCrypto.CanaryKeyContext, SecretsVaultCrypto.CanaryContext);
        store.Add(new VaultCanaryEntity
        {
            WrappedKey = canary.WrappedKey.CipherText, KeyNonce = canary.WrappedKey.Nonce, KeyTag = canary.WrappedKey.Tag,
            CipherText = canary.Value.CipherText, Nonce = canary.Value.Nonce, Tag = canary.Value.Tag,
            Digest = canary.Digest,
        });
    }

    /// <summary>The product's data key, unwrapped; made and staged when the product has none. The caller clears it.</summary>
    private async Task<byte[]> DataKeyAsync(byte[] root, Guid productId, CancellationToken ct)
    {
        if (await store.GetProductKeyAsync(productId, ct) is { } stored)
            return cipher.Decrypt(Payload(stored.WrappedKey, stored.Nonce, stored.Tag), root, SecretsVaultCrypto.ProductKeyContext(productId));
        var key = SecretsVaultCrypto.NewKey();
        var wrapped = cipher.Encrypt(key, root, SecretsVaultCrypto.ProductKeyContext(productId));
        store.Add(new VaultProductKeyEntity { ProductId = productId, WrappedKey = wrapped.CipherText, Nonce = wrapped.Nonce, Tag = wrapped.Tag });
        return key;
    }

    /// <summary>Stages one new version under an unwrapped data key.</summary>
    private async Task<SecretWritten> StageVersionAsync(
        Guid productId, DeploymentEnvironment environment, SecretWrite write, byte[] dataKey, string actor, string? origin,
        CancellationToken ct)
    {
        var secret = await store.GetSecretAsync(productId, environment, write.Key, ct);
        if (secret is null)
        {
            secret = new VaultSecretEntity { Id = Guid.CreateVersion7(), ProductId = productId, Environment = environment, Key = write.Key };
            store.Add(secret);
        }
        var version = secret.CurrentVersion + 1;
        var sealedValue = cipher.EncryptText(write.Value, dataKey, SecretsVaultCrypto.ValueContext(productId, environment, write.Key, version));
        var fingerprint = SecretsVaultCrypto.ValueFingerprint.Hash(write.Value, dataKey);
        store.Add(new VaultSecretVersionEntity
        {
            Id = Guid.CreateVersion7(),
            SecretId = secret.Id,
            Version = version,
            CipherText = sealedValue.CipherText,
            Nonce = sealedValue.Nonce,
            Tag = sealedValue.Tag,
            Fingerprint = fingerprint,
            Note = string.IsNullOrWhiteSpace(write.Note) ? null : write.Note.Trim(),
            Origin = origin,
            CreatedBy = actor,
        });
        secret.CurrentVersion = version;
        secret.RemovedAt = null;
        if (write.Expiry is { } expiry)
            secret.ExpiresAt = expiry.At;
        return new SecretWritten(write.Key, version, fingerprint);
    }

    /// <summary>The refusal a sealed vault gives.</summary>
    /// <param name="reason">Why it is sealed.</param>
    /// <returns>The error.</returns>
    public static AppError Sealed(string? reason) => AppErrorFactory.ExternalUnavailable(reason switch
    {
        SecretsVaultStates.Invalid => "The vault is sealed: its root key file holds no base64 256-bit key.",
        SecretsVaultStates.Mismatch => "The vault is sealed: its root key file belongs to another vault.",
        _ => "The vault is sealed: its root key file is missing.",
    });

    private static EncryptedPayload Payload(byte[] cipherText, byte[] nonce, byte[] tag) =>
        new() { CipherText = cipherText, Nonce = nonce, Tag = tag };

    [LoggerMessage(Level = LogLevel.Warning, Message = "The vault's root key file does not open its canary; the vault stays sealed")]
    private static partial void LogMismatch(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "A vault secret or data key did not decrypt")]
    private static partial void LogUndecryptable(ILogger logger);
}
