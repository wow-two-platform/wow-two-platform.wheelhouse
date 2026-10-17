using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ServiceStatusUpdateCommand"/>. The service key is taken as given: the repository declares a
/// product's services, and Wheelhouse does not always hold that list.</summary>
public sealed class ServiceStatusUpdateCommandHandler(
    IProductsRepository products, IServiceStatusesRepository statuses, ProductsProjectionService projection)
    : ICommandHandler<ServiceStatusUpdateCommand, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(ServiceStatusUpdateCommand request, CancellationToken cancellationToken)
    {
        var product = await products.FindAsync(request.Product, cancellationToken);
        if (product is null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Product}' was not found."));

        await statuses.SetAsync(product.Id, request.Service, request.Status, cancellationToken);
        return AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, cancellationToken));
    }
}
