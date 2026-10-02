using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Queries;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Products.QueryHandlers;

/// <summary>Handles <see cref="ProductGetBySlugQuery"/>.</summary>
public sealed class ProductGetBySlugQueryHandler(IProductsRepository products, ProductsProjectionService projection)
    : IQueryHandler<ProductGetBySlugQuery, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(ProductGetBySlugQuery request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Slug, cancellationToken);
        return product is null
            ? AppResult<ProductDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."))
            : AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, cancellationToken));
    }
}
