namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines a read of a standalone Secrets Vault's database — its namespaces, wrapped data keys, secrets and
/// encrypted versions, as stored — for an import. Nothing is decrypted here.</summary>
public interface IStandaloneVaultReader
{
    /// <summary>Reads every namespace with its keys, secrets and versions.</summary>
    /// <param name="connectionString">The standalone vault's database, or a restore of its dump; never logged.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The store's contents.</returns>
    Task<StandaloneVaultStore> ReadAsync(string connectionString, CancellationToken ct);
}
