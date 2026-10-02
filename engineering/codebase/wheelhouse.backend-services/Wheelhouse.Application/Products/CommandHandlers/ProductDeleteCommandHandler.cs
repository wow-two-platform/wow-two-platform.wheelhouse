using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductDeleteCommand"/>; a product a target still runs stays.</summary>
public sealed class ProductDeleteCommandHandler(IProductsRepository products, ITargetsRepository targets)
    : ICommandHandler<ProductDeleteCommand, AppResult<Unit>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<Unit>> HandleAsync(ProductDeleteCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Slug, cancellationToken);
        if (product is null)
            return AppResult<Unit>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."));
        if (await targets.AnyForProductAsync(product.Id, cancellationToken))
            return AppResult<Unit>.Fail(AppErrorFactory.Conflict("Remove the product's environments first."));

        await products.DeleteAsync(product, cancellationToken);
        return AppResult<Unit>.Ok(default);
    }
}
