using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.SecretsVault.Models;

namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents one value the owner gives an environment: a secret goes to the vault, a setting to the store.</summary>
/// <param name="Key">The configuration path.</param>
/// <param name="Value">The value; empty when <paramref name="Remove"/>.</param>
/// <param name="Secret">Whether it is a secret.</param>
public sealed record DeploySettingValue(string Key, string Value, bool Secret)
{
    /// <summary>Gets the owner's note on a secret's change, kept with its version.</summary>
    public string? Note { get; init; }

    /// <summary>Gets a secret's expiry to set; null keeps the one it has.</summary>
    public ExpiryChange? Expiry { get; init; }

    /// <summary>Gets whether the owner confirmed a key that reads as a secret is a plain setting.</summary>
    public bool ConfirmPlain { get; init; }

    /// <summary>Gets whether to remove the key's value, from the store and the vault alike, instead of setting one.</summary>
    public bool Remove { get; init; }

    /// <summary>Gets the format of a secret Wheelhouse generates instead of taking a value (<see cref="SecretValues"/>
    /// names them); null for a given value. A generated value goes to the vault only, never back to the caller.</summary>
    public string? Generate { get; init; }

    /// <summary>Returns the value's key and kind only, so logs and exceptions cannot print a value.</summary>
    public override string ToString() => nameof(DeploySettingValue) + " { " + Key + (Secret ? ", secret" : "")
        + (Generate is null ? "" : ", generate " + Generate) + (Remove ? ", remove" : "") + " }";
}
