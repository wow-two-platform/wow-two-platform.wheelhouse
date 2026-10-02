using Wheelhouse.Domain.Vaults.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of the secrets vaults Wheelhouse administers.</summary>
public interface IVaultsRepository : IRepository<VaultEntity, Guid>
{
    /// <summary>Finds the vault a slug names, tracked for changes.</summary>
    /// <param name="slug">The vault's slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The vault, or <c>null</c> when none has the slug.</returns>
    Task<VaultEntity?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Checks whether any vault runs on a server.</summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> while a vault still runs there.</returns>
    Task<bool> AnyForServerAsync(Guid serverId, CancellationToken ct = default);
}
