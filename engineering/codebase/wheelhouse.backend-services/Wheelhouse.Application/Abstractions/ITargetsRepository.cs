using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of each product's environments on their servers.</summary>
public interface ITargetsRepository : IRepository<TargetEntity, Guid>
{
    /// <summary>Finds the target a slug names, tracked for changes.</summary>
    /// <param name="slug">The target's slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The target, or <c>null</c> when none has the slug.</returns>
    Task<TargetEntity?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Checks whether any target runs a product.</summary>
    /// <param name="productId">The product's identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> while a target still runs it.</returns>
    Task<bool> AnyForProductAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Checks whether any target runs on a server.</summary>
    /// <param name="serverId">The server's identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> while a target still runs there.</returns>
    Task<bool> AnyForServerAsync(Guid serverId, CancellationToken ct = default);
}
