using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Queries;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.QueryHandlers;

/// <summary>Handles <see cref="ProductDescriptorQuery"/> for a product the inventory holds; a descriptor that cannot be
/// read is an answer with its reason, not a failure.</summary>
public sealed class ProductDescriptorQueryHandler(IProductsRepository products, ProductDescriptorService descriptors)
    : IQueryHandler<ProductDescriptorQuery, AppResult<ProductDescriptorDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDescriptorDto>> HandleAsync(
        ProductDescriptorQuery request, CancellationToken cancellationToken) =>
        await products.GetBySlugAsync(request.Slug, cancellationToken) is { } product
            ? AppResult<ProductDescriptorDto>.Ok(await descriptors.ReadAsync(product, cancellationToken))
            : AppResult<ProductDescriptorDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."));
}
