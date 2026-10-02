using Wheelhouse.Domain.Integrations.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of integration keys, kept by the hash of their secret; the secret is never stored.</summary>
public interface IIntegrationKeysRepository
{
    /// <summary>Adds a key.</summary>
    /// <param name="key">The key to add.</param>
    /// <param name="ct">A cancellation token.</param>
    Task AddAsync(IntegrationKeyEntity key, CancellationToken ct = default);

    /// <summary>Lists every key, newest first, revoked ones included.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The keys.</returns>
    Task<IReadOnlyList<IntegrationKeyEntity>> ListAsync(CancellationToken ct = default);

    /// <summary>Finds a key by its identifier.</summary>
    /// <param name="id">The key's identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The key, or <c>null</c> when none has the identifier.</returns>
    Task<IntegrationKeyEntity?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Saves a key's changes.</summary>
    /// <param name="key">The changed key.</param>
    /// <param name="ct">A cancellation token.</param>
    Task UpdateAsync(IntegrationKeyEntity key, CancellationToken ct = default);
}
