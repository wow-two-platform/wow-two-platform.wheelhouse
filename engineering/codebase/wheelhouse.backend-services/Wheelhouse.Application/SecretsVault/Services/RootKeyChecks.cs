using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Domain.SecretsVault.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Services;

/// <summary>The root key's two scheduled checks (decision 67). The owner's offline copy: Wheelhouse shows the key in use's
/// fingerprint, the owner compares it with their copy's and marks the copy verified; the mark counts for
/// <see cref="SecretsVaultSettings.OfflineCopyDays"/> and only while the key in use keeps that fingerprint. The key file:
/// a periodic pass checks it is present, readable by the console alone and opens the canary, and keeps the result.</summary>
/// <remarks>The key never leaves this service: only its fingerprint does, and nothing logs either.</remarks>
public sealed partial class RootKeyChecks(
    SecretsVaultService vault,
    ISecretsVaultStore store,
    IMasterKeyBroker broker,
    MasterKeyProofTracker proofs,
    IVaultRootKeyVerifications verifications,
    RootKeyFileMonitor monitor,
    SecretsVaultSettings settings,
    TimeProvider time,
    ILogger<RootKeyChecks> logger)
{
    /// <summary>Checks the key file once: present, readable by the console, closed to everyone else, and opening the
    /// canary afresh rather than by its remembered proof. The result replaces the last; a failure is an issue until a
    /// check passes. A sealing problem outranks an exposed mode.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The check.</returns>
    public async Task<RootKeyFileCheckDto> CheckFileAsync(CancellationToken ct)
    {
        var problem = broker.CheckAccess();
        if (problem is not (MasterKeyRead.Missing or MasterKeyRead.Unreadable))
        {
            proofs.Forget();
            var (root, reason) = await vault.UnsealAsync(ct);
            if (root is not null)
                CryptographicOperations.ZeroMemory(root);
            problem = reason ?? problem;
        }
        var check = new RootKeyFileCheckDto(time.GetUtcNow(), problem);
        monitor.Record(check);
        if (problem is not null)
            LogFileCheckFailed(logger, problem);
        return check;
    }

    /// <summary>Whether anything is encrypted under the root key yet: the canary or a product's data key. Before that, a
    /// lost key costs nothing, so neither check raises an issue.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> once the vault's first write bound the key.</returns>
    public async Task<bool> IsBoundAsync(CancellationToken ct) =>
        await store.GetCanaryAsync(ct) is not null || (await store.GetProductKeysAsync(ct)).Count > 0;

    /// <summary>Reads the offline copy's state: the key in use's fingerprint and its last verification.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The state.</returns>
    public async Task<RootKeyOfflineCopyDto> OfflineCopyAsync(CancellationToken ct)
    {
        var (fingerprint, _) = await FingerprintAsync(ct);
        return Describe(fingerprint, await verifications.GetLatestAsync(ct), await IsBoundAsync(ct), time.GetUtcNow(),
            settings.OfflineCopyDays);
    }

    /// <summary>Records that the owner compared their offline copy with the key in use and they match. Refused while
    /// sealed, and when the key in use no longer has the fingerprint the owner compared.</summary>
    /// <param name="fingerprint">The fingerprint the owner compared, as the console showed it.</param>
    /// <param name="reason">How the owner checked it.</param>
    /// <param name="actor">Who marks it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The offline copy's new state, or why nothing was recorded.</returns>
    public async Task<AppResult<RootKeyOfflineCopyDto>> VerifyOfflineCopyAsync(
        string fingerprint, string reason, string actor, CancellationToken ct)
    {
        var (current, sealedReason) = await FingerprintAsync(ct);
        if (current is null)
            return AppResult<RootKeyOfflineCopyDto>.Fail(SecretsVaultService.Sealed(sealedReason));
        if (!string.Equals(current, fingerprint, StringComparison.Ordinal))
            return AppResult<RootKeyOfflineCopyDto>.Fail(AppErrorFactory.Conflict(
                "The key in use no longer has the fingerprint you compared; read it again and compare your copy with it."));
        await verifications.AddAsync(new VaultRootKeyVerificationEntity
        {
            Id = Guid.CreateVersion7(), Fingerprint = current, VerifiedBy = actor, Reason = reason.Trim(), VerifiedAt = time.GetUtcNow(),
        }, ct);
        return AppResult<RootKeyOfflineCopyDto>.Ok(await OfflineCopyAsync(ct));
    }

    /// <summary>Describes the offline copy: the newest verification counts only while it names the key in use, for
    /// <paramref name="days"/> days; a verification is due once the key is bound and none counts.</summary>
    /// <param name="fingerprint">The key in use's fingerprint; null while sealed.</param>
    /// <param name="latest">The newest verification.</param>
    /// <param name="bound">Whether anything is encrypted under the key.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="days">How many days a verification counts.</param>
    /// <returns>The state.</returns>
    public static RootKeyOfflineCopyDto Describe(
        string? fingerprint, VaultRootKeyVerificationEntity? latest, bool bound, DateTimeOffset now, int days)
    {
        var counting = fingerprint is not null && latest is not null && latest.Fingerprint == fingerprint ? latest : null;
        var dueAt = counting?.VerifiedAt.AddDays(days);
        return new RootKeyOfflineCopyDto(fingerprint, counting?.VerifiedAt, counting?.VerifiedBy, counting?.Reason,
            fingerprint is not null && latest is not null && counting is null, dueAt,
            fingerprint is not null && bound && (dueAt is not { } at || now >= at), days);
    }

    /// <summary>The key in use's fingerprint, or why the vault is sealed.</summary>
    private async Task<(string? Fingerprint, string? Reason)> FingerprintAsync(CancellationToken ct)
    {
        var (root, reason) = await vault.UnsealAsync(ct);
        if (root is null)
            return (null, reason);
        try
        {
            return (SecretsVaultCrypto.RootKeyFingerprint.Hash(root), null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The root key file check failed ({Problem}); Issues name it until a check passes")]
    private static partial void LogFileCheckFailed(ILogger logger, string problem);
}
