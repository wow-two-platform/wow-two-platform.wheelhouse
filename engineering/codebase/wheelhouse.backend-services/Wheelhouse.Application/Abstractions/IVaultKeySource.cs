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

    /// <summary>Checks the file without reading the key: present, readable by the console, and closed to everyone but its
    /// owner — which, since the console reads it, is the console's own user.</summary>
    /// <returns><c>missing</c>, <c>unreadable</c> or <c>exposed</c>; null when the file passes.</returns>
    string? CheckFile();
}
