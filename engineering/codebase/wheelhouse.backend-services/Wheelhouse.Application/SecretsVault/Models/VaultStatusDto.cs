namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents the built-in vault's state for the console: sealed or not and why, where the root key is read
/// from, how much it holds, and the root key's checks. Never a key or a value.</summary>
/// <param name="Mode">Where deploys take secrets from: <c>builtin</c>, or <c>external</c> while the fallback is on.</param>
/// <param name="State"><c>unsealed</c> or <c>sealed</c>.</param>
/// <param name="Reason">Why it is sealed: <c>missing</c>, <c>invalid</c> or <c>mismatch</c>; null when unsealed.</param>
/// <param name="KeyFile">Where the root key file is read from.</param>
/// <param name="Products">How many products hold a data key.</param>
/// <param name="Secrets">How many secrets it holds, removed ones left out.</param>
/// <param name="Expiring">How many lapse within 30 days.</param>
/// <param name="Expired">How many have lapsed.</param>
/// <param name="OfflineCopy">The root key in use's fingerprint and the owner's last check of their offline copy.</param>
/// <param name="KeyFileCheck">The last scheduled check of the root key file; null before the first.</param>
public sealed record VaultStatusDto(
    string Mode, string State, string? Reason, string KeyFile, int Products, int Secrets, int Expiring, int Expired,
    RootKeyOfflineCopyDto? OfflineCopy = null, RootKeyFileCheckDto? KeyFileCheck = null);
