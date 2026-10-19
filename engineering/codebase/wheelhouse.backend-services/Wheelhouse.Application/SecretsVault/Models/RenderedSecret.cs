namespace Wheelhouse.Application.SecretsVault.Models;

/// <summary>Represents a secret value read for a deploy's render, with the version it came from.</summary>
/// <param name="Value">The value; goes to the runner's stdin only, never logged.</param>
/// <param name="Version">The version read; null when the vault does not say.</param>
/// <param name="Fingerprint">Its keyed fingerprint; null when the vault does not keep one.</param>
public sealed record RenderedSecret(string Value, int? Version, string? Fingerprint)
{
    /// <summary>Returns the secret without its value.</summary>
    public override string ToString() => nameof(RenderedSecret) + " { v" + Version + " }";
}
