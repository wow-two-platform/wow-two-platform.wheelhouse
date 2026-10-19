namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents one setting a service takes on a target, and where the next deploy's render takes it from.</summary>
/// <param name="Key">The configuration path, such as <c>DataProtection:ApplicationName</c>.</param>
/// <param name="Required">Whether the release requires it: a deploy waits until a layer sets it.</param>
/// <param name="Secret">Whether its value belongs in the vault: the release's declaration, else a guess from its name.</param>
/// <param name="Source">Where its value comes from (<see cref="Constants.SettingSources"/>).</param>
/// <param name="Value">The value for a stored, default or derived setting; never a secret's.</param>
/// <param name="Default">The release's default for an optional key.</param>
/// <param name="Derived">What the render derives it from: <c>hosts</c>, <c>proxies</c>, <c>sites</c> or <c>database</c>.</param>
/// <param name="Allowed">The values the release allows, when it lists them.</param>
public sealed record DeploySettingDto(
    string Key, bool Required, bool Secret, string Source, string? Value = null, string? Default = null,
    string? Derived = null, IReadOnlyList<string>? Allowed = null);
