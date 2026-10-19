namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents what the console may know about a secret: its version, who changed it and when, its keyed
/// fingerprint and its expiry. Never its value.</summary>
/// <param name="Key">The configuration path.</param>
/// <param name="Version">The version a render reads.</param>
/// <param name="UpdatedAt">When that version was written.</param>
/// <param name="UpdatedBy">Who wrote it.</param>
/// <param name="Fingerprint">Its keyed fingerprint: equal values of a product show equal fingerprints.</param>
/// <param name="ExpiresAt">When it lapses, if the owner set an expiry.</param>
/// <param name="Expiry">How close that is: <c>expired</c>, <c>7d</c>, <c>30d</c>, or null.</param>
public sealed record SecretMetadataDto(
    string Key, int Version, DateTimeOffset UpdatedAt, string UpdatedBy, string Fingerprint, DateTimeOffset? ExpiresAt,
    string? Expiry);
