namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents a version a write made.</summary>
/// <param name="Key">The configuration path.</param>
/// <param name="Version">The new version.</param>
/// <param name="Fingerprint">Its keyed fingerprint.</param>
public sealed record SecretWritten(string Key, int Version, string Fingerprint);
