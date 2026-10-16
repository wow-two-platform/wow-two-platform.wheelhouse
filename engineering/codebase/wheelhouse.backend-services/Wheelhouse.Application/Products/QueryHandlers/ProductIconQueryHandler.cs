using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.QueryHandlers;

/// <summary>Handles <see cref="ProductIconQuery"/>: lists the repository's icon files, picks the one the icon
/// convention resolves to, and reads it. Not found when no product has the slug or no file resolves, so the console
/// shows its monogram.</summary>
public sealed class ProductIconQueryHandler(IProductsRepository products, IProductIconSource icons)
    : IQueryHandler<ProductIconQuery, AppResult<ProductIconImage>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductIconImage>> HandleAsync(ProductIconQuery request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Slug, cancellationToken);
        if (product is null)
            return AppResult<ProductIconImage>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."));

        var paths = await icons.ListAsync(product.Repository, product.DefaultBranch, cancellationToken);
        var path = ProductIconPaths.Choose(paths, request.Icon);
        var icon = path is null ? null : await icons.ReadAsync(product.Repository, product.DefaultBranch, path, cancellationToken);
        return icon is null
            ? AppResult<ProductIconImage>.Fail(AppErrorFactory.NotFound("The product's repository carries no icon."))
            : AppResult<ProductIconImage>.Ok(icon);
    }
}
