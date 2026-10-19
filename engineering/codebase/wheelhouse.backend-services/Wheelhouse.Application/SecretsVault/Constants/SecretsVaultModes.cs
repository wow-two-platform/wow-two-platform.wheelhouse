namespace Wheelhouse.Application.SecretsVault.Constants;

/// <summary>Names where a deploy's render takes secrets from: the built-in vault, or for one release after the merge,
/// the standalone vaults the inventory lists (<c>Vault:Mode</c>).</summary>
public static class SecretsVaultModes
{
    /// <summary>The built-in Secrets Vault module: in-process, one database, one transaction with settings.</summary>
    public const string BuiltIn = "builtin";

    /// <summary>The fallback: inventory vaults over their management API with the administrator password file.</summary>
    public const string External = "external";
}
