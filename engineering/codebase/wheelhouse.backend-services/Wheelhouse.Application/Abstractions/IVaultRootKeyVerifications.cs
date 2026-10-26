using Wheelhouse.Domain.SecretsVault.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the record of the owner's offline root key copy checks.</summary>
public interface IVaultRootKeyVerifications
{
    /// <summary>Gets the newest verification, or null when the owner never marked one.</summary>
    Task<VaultRootKeyVerificationEntity?> GetLatestAsync(CancellationToken ct);

    /// <summary>Saves a verification.</summary>
    Task AddAsync(VaultRootKeyVerificationEntity verification, CancellationToken ct);
}
