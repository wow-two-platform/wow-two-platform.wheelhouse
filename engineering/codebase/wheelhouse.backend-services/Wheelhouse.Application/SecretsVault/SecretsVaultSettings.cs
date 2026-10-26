namespace Wheelhouse.Application.SecretsVault;

/// <summary>Holds the built-in Secrets Vault's host settings (<c>Vault</c> section): where the root key file is, how often
/// the owner's offline copy is to be verified, and for one release after the merge, whether deploys still take secrets
/// from the inventory's standalone vaults.</summary>
public sealed record SecretsVaultSettings
{
    /// <summary>Gets or sets where deploys take secrets from: <c>builtin</c> (the default) or <c>external</c>, the
    /// fallback to the standalone vaults kept for one release.</summary>
    public string Mode { get; set; } = Constants.SecretsVaultModes.BuiltIn;

    /// <summary>Gets or sets the root key file: a base64 256-bit key, root-only on the control host and mounted into the
    /// console. Empty means <c>{Deployment:Root}/vault/root.key</c>. A missing file leaves the vault sealed.</summary>
    public string KeyFile { get; set; } = "";

    /// <summary>Gets or sets how many days the owner's offline copy of the root key counts as verified; past that, Issues
    /// remind them to compare it again (default 90, 1–3650).</summary>
    public int OfflineCopyDays { get; set; } = 90;

    /// <summary>Gets whether the fallback to the standalone vaults is on.</summary>
    public bool IsExternal => string.Equals(Mode, Constants.SecretsVaultModes.External, StringComparison.OrdinalIgnoreCase);
}
