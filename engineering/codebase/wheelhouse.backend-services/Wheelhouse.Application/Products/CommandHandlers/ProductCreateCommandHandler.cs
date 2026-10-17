using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using Wheelhouse.Application.Workspaces.Services;
using Wheelhouse.Domain.Products.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductCreateCommand"/>; a new product starts as building, in the workspace the request
/// names or else the default one.</summary>
public sealed class ProductCreateCommandHandler(
    IProductsRepository products,
    IWorkspacesRepository workspaces,
    DefaultWorkspaceService defaultWorkspace,
    ProductsProjectionService projection)
    : ICommandHandler<ProductCreateCommand, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(ProductCreateCommand request, CancellationToken cancellationToken)
    {
        if (await products.GetBySlugAsync(request.Slug, cancellationToken) is not null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.Conflict($"A product is already named '{request.Slug}'."));
        var workspace = request.WorkspaceId is { } workspaceId
            ? await workspaces.GetByIdAsync(workspaceId, cancellationToken)
            : await defaultWorkspace.GetAsync(cancellationToken);
        if (workspace is null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.Validation($"Workspace '{request.WorkspaceId}' was not found."));

        var product = await products.CreateAsync(new ProductEntity
        {
            Id = Guid.CreateVersion7(),
            Slug = request.Slug,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Repository = request.Repository,
            DefaultBranch = request.DefaultBranch,
            WorkspaceId = workspace.Id,
            Release = request.Release,
        }, cancellationToken);
        return AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, cancellationToken));
    }
}
