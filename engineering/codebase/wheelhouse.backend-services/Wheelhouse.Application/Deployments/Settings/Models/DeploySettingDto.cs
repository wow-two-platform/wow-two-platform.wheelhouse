using Wheelhouse.Application.SecretsVault.Models;

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
    string? Derived = null, IReadOnlyList<string>? Allowed = null)
{
    /// <summary>Gets what its value must look like: <c>text</c>, <c>number</c>, <c>boolean</c>, <c>choice</c>, <c>url</c>,
    /// <c>cron</c>, <c>date</c> or <c>email</c>.</summary>
    public string Type { get; init; } = "text";

    /// <summary>Gets a date key's format, such as <c>yyyy-MM-dd</c>.</summary>
    public string? Format { get; init; }

    /// <summary>Gets whether the owner added the key in the console rather than a release declaring it.</summary>
    public bool AdHoc { get; init; }

    /// <summary>Gets whether every service takes a key the owner added even while no release declares it.</summary>
    public bool EveryService { get; init; }

    /// <summary>Gets whether the next deploy holds the key's value from every service: no service of the release declares
    /// it, and the owner did not add it for every service. It reaches a service once a release that declares it deploys.</summary>
    public bool Held { get; init; }

    /// <summary>Gets whether its name reads as a secret, so saving it as a plain setting needs a confirmation.</summary>
    public bool LooksSecret { get; init; }

    /// <summary>Gets a vault secret's metadata — version, who and when, fingerprint, expiry; never its value.</summary>
    public SecretMetadataDto? Vault { get; init; }

    /// <summary>Gets how Wheelhouse makes the key's value: the release's <c>generate</c> declaration, never a value. A deploy
    /// fills a missing one; Regenerate writes a new version.</summary>
    public SettingGenerateDto? Generate { get; init; }

    /// <summary>Gets the configuration path a linked key takes its value from, per environment; null for a key of its
    /// own. A linked key is read-only: it takes its source's type and secrecy, and a save to it is refused.</summary>
    public string? From { get; init; }

    /// <summary>Gets the other product's key the owner linked this key to, in the same environment; null for a key not
    /// linked so. Each deploy renders the source's current value under this key, which wins over a value of its own;
    /// <see cref="Secret"/> and <see cref="Vault"/> then describe the source, and a save to the key is refused until it is
    /// unlinked.</summary>
    public SettingLinkDto? Link { get; init; }
}
