using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductWorkspaceUpdateCommand"/>.</summary>
public sealed class ProductWorkspaceUpdateCommandHandler(
    IProductsRepository products, IWorkspacesRepository workspaces, ProductsProjectionService projection)
    : ICommandHandler<ProductWorkspaceUpdateCommand, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(
        ProductWorkspaceUpdateCommand request, CancellationToken cancellationToken)
    {
        var product = await products.FindAsync(request.Product, cancellationToken);
        if (product is null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Product}' was not found."));
        if (!await workspaces.ExistsAsync(request.WorkspaceId, cancellationToken))
            return AppResult<ProductDto>.Fail(AppErrorFactory.Validation($"Workspace '{request.WorkspaceId}' was not found."));

        product.WorkspaceId = request.WorkspaceId;
        await products.UpdateAsync(product, cancellationToken);
        return AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, cancellationToken));
    }
}
