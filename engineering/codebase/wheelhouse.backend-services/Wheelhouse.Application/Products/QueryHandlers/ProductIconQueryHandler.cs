using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Queries;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Products.QueryHandlers;

/// <summary>Handles <see cref="ProductIconQuery"/>; not found when no product has the slug or its repository carries
/// no icon.</summary>
public sealed class ProductIconQueryHandler(IProductsRepository products, IProductIconSource icons)
    : IQueryHandler<ProductIconQuery, AppResult<ProductIconImage>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductIconImage>> HandleAsync(ProductIconQuery request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Slug, cancellationToken);
        if (product is null)
            return AppResult<ProductIconImage>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."));

        var icon = await icons.FindAsync(product.Repository, cancellationToken);
        return icon is null
            ? AppResult<ProductIconImage>.Fail(AppErrorFactory.NotFound("The product's repository carries no icon."))
            : AppResult<ProductIconImage>.Ok(icon);
    }
}
