using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Queries;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Products.QueryHandlers;

/// <summary>Handles <see cref="ProductGetAllQuery"/>.</summary>
public sealed class ProductGetAllQueryHandler(IProductsRepository products, ProductsProjectionService projection)
    : IQueryHandler<ProductGetAllQuery, AppResult<IReadOnlyList<ProductDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<ProductDto>>> HandleAsync(
        ProductGetAllQuery request, CancellationToken cancellationToken) =>
        AppResult<IReadOnlyList<ProductDto>>.Ok(
            await projection.ProjectAsync(await products.GetAllAsync(cancellationToken), cancellationToken));
}
