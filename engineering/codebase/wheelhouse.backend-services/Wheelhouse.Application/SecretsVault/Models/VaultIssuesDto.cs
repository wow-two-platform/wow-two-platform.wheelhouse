namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents what the built-in vault needs the owner for: a sealed vault, secrets lapsing or lapsed, an offline
/// root key copy due a check, and a failed check of the root key file.</summary>
/// <param name="State"><c>unsealed</c> or <c>sealed</c>.</param>
/// <param name="Reason">Why it is sealed; null when unsealed.</param>
/// <param name="Expiries">Secrets lapsing within 30 days or lapsed, soonest first.</param>
/// <param name="OfflineCopy">The owner's offline copy of the root key, while a verification of it is due.</param>
/// <param name="KeyFile">The last scheduled check of the root key file, while it failed in a way the sealed state does
/// not already say.</param>
public sealed record VaultIssuesDto(
    string State, string? Reason, IReadOnlyList<VaultExpiryDto> Expiries, RootKeyOfflineCopyDto? OfflineCopy = null,
    RootKeyFileCheckDto? KeyFile = null);
