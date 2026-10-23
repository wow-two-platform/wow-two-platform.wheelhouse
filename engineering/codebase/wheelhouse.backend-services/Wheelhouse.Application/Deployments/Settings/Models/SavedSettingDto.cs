namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents one key a save touched.</summary>
/// <param name="Key">The configuration path.</param>
/// <param name="Secret">Whether it went to the vault.</param>
/// <param name="Removed">Whether its value was removed.</param>
/// <param name="Version">A secret's new version, or the version a removed one was on.</param>
/// <param name="Fingerprint">A written secret's keyed fingerprint.</param>
/// <param name="Generated">Whether Wheelhouse generated the secret's value; it never travels back.</param>
public sealed record SavedSettingDto(
    string Key, bool Secret, bool Removed, int? Version = null, string? Fingerprint = null, bool Generated = false);
