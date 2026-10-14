using Wheelhouse.Domain.Products.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of the cached product manifests — one row per product.</summary>
public interface IProductManifestsRepository
{
    /// <summary>Reads the cached manifests of some products.</summary>
    /// <param name="productIds">The products' identifiers.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each product's cached manifest, keyed by product; a product with none is absent.</returns>
    Task<IReadOnlyDictionary<Guid, ProductManifestEntity>> GetByProductsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);

    /// <summary>Stores a product's manifest, replacing the one it had. A write that loses a race to an identical write
    /// for the same product changes nothing and does not fail.</summary>
    /// <param name="manifest">The manifest to keep.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SaveAsync(ProductManifestEntity manifest, CancellationToken ct = default);

    /// <summary>Drops a product's cached manifest, if it has one.</summary>
    /// <param name="productId">The product's identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    Task DeleteAsync(Guid productId, CancellationToken ct = default);
}
