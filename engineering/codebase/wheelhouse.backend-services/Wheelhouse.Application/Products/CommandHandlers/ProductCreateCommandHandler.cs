using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Services;
using Wheelhouse.Application.Products;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using Wheelhouse.Application.Workspaces.Services;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Domain.Products.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductCreateCommand"/>; a new product starts active, in the workspace the request
/// names or else the default one, deployed by Wheelhouse unless the request says otherwise. Each repository that
/// publishes takes the release source its repository gives (<see cref="ProductReleaseDerivation"/>).</summary>
public sealed class ProductCreateCommandHandler(
    IProductsRepository products,
    IWorkspacesRepository workspaces,
    DefaultWorkspaceService defaultWorkspace,
    ProductsProjectionService projection,
    CatalogReleaseService releases)
    : ICommandHandler<ProductCreateCommand, AppResult<ProductDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductDto>> HandleAsync(ProductCreateCommand request, CancellationToken cancellationToken)
    {
        if (await products.GetBySlugAsync(request.Slug, cancellationToken) is not null)
            return AppResult<ProductDto>.Fail(AppErrorFactory.Conflict($"A product is already named '{request.Slug}'."));
        var resolved = await ProductReleaseDerivation.ResolveAsync(releases, request.Slug, request.DefaultBranch,
        [
            new(request.Repository, IsSet: true, request.Release),
            .. (request.Components ?? []).Select(component =>
                new ProductReleaseDerivation.Entry(component.Repository, IsSet: true, ProductReleaseRequest.Of(component.Release))),
        ], [], cancellationToken);
        if (resolved.Refusal is { } refusal)
            return AppResult<ProductDto>.Fail(AppErrorFactory.Validation(refusal));
        if (ProductRepositories.Problem(request.Slug, resolved.Repositories) is { } problem)
            return AppResult<ProductDto>.Fail(AppErrorFactory.Validation(problem));
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
            Release = resolved.Repositories[0].Release,
            Components = [.. resolved.Repositories.Skip(1).Select(item => new ProductComponentValueObject
            {
                Repository = item.Repository, Release = item.Release,
            })],
            Deploy = request.Deploy ?? ProductDeployMode.Wheelhouse,
        }, cancellationToken);
        return AppResult<ProductDto>.Ok(await projection.ProjectAsync(product, cancellationToken));
    }
}
