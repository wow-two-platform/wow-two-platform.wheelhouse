using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using Wheelhouse.Domain.Products.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductUpdateCommand"/>. While the product's repository carries a valid manifest, the
/// manifest owns the name and description: a save leaves the row's own values as they are.</summary>
public sealed class ProductUpdateCommandHandler(
    IProductsRepository products, ProductIdentityService identities, ProductsProjectionService projection)
    : ICommandHandler<ProductUpdateCommand, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(ProductUpdateCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Slug, cancellationToken);
        if (product is null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."));

        // The repositories this save leaves the product with must stand together: a service belongs to one of them.
        var components = request.Components ?? product.Components;
        if (ProductRepositories.Problem(product.Slug, ProductRepositories.Of(request.Repository, request.Release, components)) is { } problem)
            return AppResult<ProductDto>.Fail(AppErrorFactory.Validation(problem));

        // Whether a manifest governs is decided for the repository and branch this save leaves the product with. A copy
        // of the row stands in for it, so nothing is half-saved while the manifest is read.
        var saved = product with { Repository = request.Repository, DefaultBranch = request.DefaultBranch, Components = components };
        var identity = (await identities.ResolveAsync([saved], cancellationToken))[product.Id];
        if (identity.Source == ProductIdentitySource.Record)
        {
            product.Name = request.Name.Trim();
            product.Description = request.Description.Trim();
        }

        product.Repository = request.Repository;
        product.DefaultBranch = request.DefaultBranch;
        product.Release = request.Release;
        product.Components = components;
        product.Deploy = request.Deploy ?? product.Deploy;
        await products.UpdateAsync(product, cancellationToken);
        return AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, cancellationToken));
    }
}
