namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines where the built-in Secrets Vault's root key comes from: a root-only file on the control host,
/// mounted into the console, never in the database.</summary>
public interface IVaultKeySource
{
    /// <summary>Gets where the key is read from, as the owner placed it; shown in the console, never the key.</summary>
    string Location { get; }

    /// <summary>Reads the root key now. A missing or malformed file leaves the vault sealed, with the reason.</summary>
    /// <returns>The key, or why there is none.</returns>
    VaultKeyRead Read();
}
