namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents a value Wheelhouse generated for a secret key: it goes to the vault and is dropped; nothing returns,
/// logs or audits it.</summary>
/// <param name="Value">The value, without a trailing newline.</param>
public sealed record GeneratedSettingValue(string Value)
{
    /// <summary>Returns the type's name only, so logs and exceptions cannot print the value.</summary>
    public override string ToString() => nameof(GeneratedSettingValue) + " { }";
}
