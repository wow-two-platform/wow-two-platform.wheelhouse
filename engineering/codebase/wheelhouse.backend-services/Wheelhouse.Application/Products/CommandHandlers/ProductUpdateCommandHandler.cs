using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductUpdateCommand"/>.</summary>
public sealed class ProductUpdateCommandHandler(IProductsRepository products, ProductsProjectionService projection)
    : ICommandHandler<ProductUpdateCommand, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(ProductUpdateCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Slug, cancellationToken);
        if (product is null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."));

        product.Name = request.Name.Trim();
        product.Description = request.Description.Trim();
        product.Repository = request.Repository;
        product.DefaultBranch = request.DefaultBranch;
        product.Release = request.Release;
        await products.UpdateAsync(product, cancellationToken);
        return AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, cancellationToken));
    }
}
