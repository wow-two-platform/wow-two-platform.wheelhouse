namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents one version in a secret's history: who wrote it, when, its fingerprint and note. Never its value.</summary>
/// <param name="Version">Its number.</param>
/// <param name="CreatedBy">Who wrote it.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="Fingerprint">Its keyed fingerprint.</param>
/// <param name="Note">The owner's note.</param>
/// <param name="Origin">Where the value came from when not typed: a restore or an import.</param>
/// <param name="Current">Whether a render reads it.</param>
public sealed record SecretVersionDto(
    int Version, string CreatedBy, DateTimeOffset CreatedAt, string Fingerprint, string? Note, string? Origin, bool Current);
