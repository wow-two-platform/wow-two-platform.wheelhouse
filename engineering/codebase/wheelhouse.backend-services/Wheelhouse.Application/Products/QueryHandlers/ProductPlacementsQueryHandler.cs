using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Queries;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.QueryHandlers;

/// <summary>Handles <see cref="ProductPlacementsQuery"/> for a product the inventory holds.</summary>
public sealed class ProductPlacementsQueryHandler(IProductsRepository products, IInfraGateway gateway)
    : IQueryHandler<ProductPlacementsQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(ProductPlacementsQuery request, CancellationToken cancellationToken) =>
        await products.GetBySlugAsync(request.Slug, cancellationToken) is null
            ? AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Product '{request.Slug}' was not found."))
            : await gateway.PlacementsAsync(request.Slug, cancellationToken);
}
