using Wheelhouse.Domain.Products.Entities;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of the portfolio's products.</summary>
public interface IProductsRepository : IRepository<ProductEntity, Guid>
{
    /// <summary>Finds the product a slug names, tracked for changes.</summary>
    /// <param name="slug">The product's slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The product, or <c>null</c> when none has the slug.</returns>
    Task<ProductEntity?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Finds the product a route names, by identifier or by slug, tracked for changes.</summary>
    /// <param name="key">The product's identifier or its slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The product, or <c>null</c> when none matches.</returns>
    Task<ProductEntity?> FindAsync(string key, CancellationToken ct = default);

    /// <summary>Checks whether any product belongs to a workspace.</summary>
    /// <param name="workspaceId">The workspace's identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> while a product still belongs to it.</returns>
    Task<bool> AnyInWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}
