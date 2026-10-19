namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents one value the owner gives an environment: a secret goes to the vault, a setting to the store.</summary>
/// <param name="Key">The configuration path.</param>
/// <param name="Value">The value.</param>
/// <param name="Secret">Whether it is a secret.</param>
public sealed record DeploySettingValue(string Key, string Value, bool Secret)
{
    /// <summary>Returns the value's key and kind only, so logs and exceptions cannot print a value.</summary>
    public override string ToString() => nameof(DeploySettingValue) + " { " + Key + (Secret ? ", secret" : "") + " }";
}
