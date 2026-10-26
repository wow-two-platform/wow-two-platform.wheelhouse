using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Services;
using Wheelhouse.Application.Products;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Domain.Products.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductUpdateCommand"/>. While the product's repository carries a valid manifest, the
/// manifest owns the name and description: a save leaves the row's own values as they are. Each repository the save
/// asks to publish takes the release source its repository gives (<see cref="ProductReleaseDerivation"/>); further
/// repositories a save leaves out keep theirs.</summary>
public sealed class ProductUpdateCommandHandler(
    IProductsRepository products,
    ProductIdentityService identities,
    ProductsProjectionService projection,
    CatalogReleaseService releases)
    : ICommandHandler<ProductUpdateCommand, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(ProductUpdateCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Slug, cancellationToken);
        if (product is null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."));

        // Each repository the save asks to publish takes what the repository gives; the further ones it leaves out stay.
        var resolved = await ProductReleaseDerivation.ResolveAsync(releases, product.Slug, request.DefaultBranch,
        [
            new(request.Repository, IsSet: true, request.Release),
            .. request.Components is { } sent
                ? sent.Select(component =>
                    new ProductReleaseDerivation.Entry(component.Repository, IsSet: true, ProductReleaseRequest.Of(component.Release)))
                : product.Components.Select(component =>
                    new ProductReleaseDerivation.Entry(component.Repository, IsSet: false, null, component.Release)),
        ], ProductRepositories.Of(product), cancellationToken);
        if (resolved.Refusal is { } refusal)
            return AppResult<ProductDto>.Fail(AppErrorFactory.Validation(refusal));

        // The repositories this save leaves the product with must stand together: a service belongs to one of them.
        if (ProductRepositories.Problem(product.Slug, resolved.Repositories) is { } problem)
            return AppResult<ProductDto>.Fail(AppErrorFactory.Validation(problem));
        IReadOnlyList<ProductComponentValueObject> components =
        [
            .. resolved.Repositories.Skip(1).Select(item => new ProductComponentValueObject { Repository = item.Repository, Release = item.Release }),
        ];

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
        product.Release = resolved.Repositories[0].Release;
        product.Components = components;
        product.Deploy = request.Deploy ?? product.Deploy;
        await products.UpdateAsync(product, cancellationToken);
        return AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, cancellationToken));
    }
}
