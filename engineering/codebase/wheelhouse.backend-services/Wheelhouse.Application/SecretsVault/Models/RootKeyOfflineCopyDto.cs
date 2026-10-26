namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents the owner's offline copy of the root key as far as Wheelhouse knows it: the fingerprint of the
/// key in use, to compare with the copy's, and the last time the owner marked the copy verified against that key.</summary>
/// <param name="Fingerprint">The key in use's fingerprint; null while the vault is sealed.</param>
/// <param name="VerifiedAt">When the owner last marked the copy verified; null when never, or when that was another key.</param>
/// <param name="VerifiedBy">Who marked it.</param>
/// <param name="Reason">How the owner said they checked it.</param>
/// <param name="Changed">Whether the last verification was of another key: the key in use changed since.</param>
/// <param name="DueAt">When the next verification is due; null when there is none to count from.</param>
/// <param name="Due">Whether a verification is due now: the vault holds keys under the root key, and the copy was never
/// verified against it or not within <paramref name="IntervalDays"/>.</param>
/// <param name="IntervalDays">How often the copy is to be verified.</param>
public sealed record RootKeyOfflineCopyDto(
    string? Fingerprint, DateTimeOffset? VerifiedAt, string? VerifiedBy, string? Reason, bool Changed, DateTimeOffset? DueAt,
    bool Due, int IntervalDays);
