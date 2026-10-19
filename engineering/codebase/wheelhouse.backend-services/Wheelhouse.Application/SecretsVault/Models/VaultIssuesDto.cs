namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents what the built-in vault needs the owner for: a sealed vault, and secrets lapsing or lapsed.</summary>
/// <param name="State"><c>unsealed</c> or <c>sealed</c>.</param>
/// <param name="Reason">Why it is sealed; null when unsealed.</param>
/// <param name="Expiries">Secrets lapsing within 30 days or lapsed, soonest first.</param>
public sealed record VaultIssuesDto(string State, string? Reason, IReadOnlyList<VaultExpiryDto> Expiries);
