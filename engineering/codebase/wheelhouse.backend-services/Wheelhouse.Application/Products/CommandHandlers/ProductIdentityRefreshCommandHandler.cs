using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductIdentityRefreshCommand"/>. The product's approved services follow its repositories'
/// <c>deploy.yml</c> at their heads, read now, so a service a repository added shows at once
/// (<see cref="ProductReleaseSourcesService"/>). A branch GitHub cannot be read for fails the command, so the owner sees
/// the refresh did not happen; the cached manifest stays.</summary>
public sealed class ProductIdentityRefreshCommandHandler(
    IProductsRepository products,
    ProductIdentityService identities,
    ProductsProjectionService projection,
    ProductReleaseSourcesService releaseSources)
    : ICommandHandler<ProductIdentityRefreshCommand, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(
        ProductIdentityRefreshCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Slug, cancellationToken);
        if (product is null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."));

        await releaseSources.SyncProductAsync(product.Slug, fresh: true, cancellationToken);
        var identity = await identities.RefreshAsync(product, cancellationToken);
        return identity is null
            ? AppResult<ProductDto>.Fail(AppErrorFactory.ExternalUnavailable(
                $"The {product.DefaultBranch} branch of {product.Repository} could not be read from GitHub."))
            : AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, identity, cancellationToken));
    }
}
