using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductIdentityRefreshCommand"/>. A branch GitHub cannot be read for fails the command,
/// so the operator sees the refresh did not happen; the cached manifest stays.</summary>
public sealed class ProductIdentityRefreshCommandHandler(
    IProductsRepository products, ProductIdentityService identities, ProductsProjectionService projection)
    : ICommandHandler<ProductIdentityRefreshCommand, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(
        ProductIdentityRefreshCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Slug, cancellationToken);
        if (product is null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."));

        var identity = await identities.RefreshAsync(product, cancellationToken);
        return identity is null
            ? AppResult<ProductDto>.Fail(AppErrorFactory.ExternalUnavailable(
                $"The {product.DefaultBranch} branch of {product.Repository} could not be read from GitHub."))
            : AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, identity, cancellationToken));
    }
}
