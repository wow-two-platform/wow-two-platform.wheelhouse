using Wheelhouse.Domain.Products.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of the portfolio's products.</summary>
public interface IProductsRepository : IRepository<ProductEntity, Guid>
{
    /// <summary>Finds the product a slug names, tracked for changes.</summary>
    /// <param name="slug">The product's slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The product, or <c>null</c> when none has the slug.</returns>
    Task<ProductEntity?> GetBySlugAsync(string slug, CancellationToken ct = default);
}
