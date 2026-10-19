namespace Wheelhouse.Application.SecretsVault.Services;

/// <summary>Remembers, for the life of the process, which root key file content the canary proved, so a read checks the
/// file's stamp rather than decrypting the canary each time. Holds no key.</summary>
public sealed class SecretsVaultKeyCache
{
    private volatile string? _proven;

    /// <summary>Whether the canary proved the key file with this stamp.</summary>
    /// <param name="stamp">The key file's stamp.</param>
    /// <returns><c>true</c> when proven.</returns>
    public bool IsProven(string stamp) => string.Equals(_proven, stamp, StringComparison.Ordinal);

    /// <summary>Records that the canary proved the key file with this stamp.</summary>
    /// <param name="stamp">The key file's stamp.</param>
    public void Prove(string stamp) => _proven = stamp;

    /// <summary>Forgets any proof, so the next read checks the canary again.</summary>
    public void Forget() => _proven = null;
}
