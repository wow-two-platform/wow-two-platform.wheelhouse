using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Queries;
using Wheelhouse.Application.Catalog.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Catalog.QueryHandlers;

/// <summary>Handles <see cref="CatalogGetQuery"/>: an entry's product is the product with its slug.</summary>
public sealed class CatalogGetQueryHandler(CatalogReader reader, IProductsRepository products)
    : IQueryHandler<CatalogGetQuery, AppResult<CatalogDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<CatalogDto>> HandleAsync(CatalogGetQuery request, CancellationToken cancellationToken)
    {
        var read = await reader.ReadAsync(cancellationToken);
        if (read is not AppResult<CatalogSnapshot>.Success { Data: var snapshot })
            return AppResult<CatalogDto>.Fail(((AppResult<CatalogSnapshot>.Failure)read).Error);

        var bySlug = (await products.GetAllAsync(cancellationToken)).ToDictionary(product => product.Slug, product => product.Id);
        return AppResult<CatalogDto>.Ok(new CatalogDto
        {
            Source = snapshot.Source,
            Workspaces =
            [
                .. snapshot.Document.Workspaces.Select(workspace => new CatalogWorkspaceDto
                {
                    Slug = workspace.Slug, Name = workspace.Name, Description = workspace.Description,
                }),
            ],
            Entries =
            [
                .. snapshot.Document.Entries.Select(entry => new CatalogEntryDto
                {
                    Slug = entry.Slug,
                    Name = entry.Name,
                    Workspace = entry.Workspace,
                    Kind = entry.Kind,
                    Deploy = entry.Deploy,
                    Purpose = entry.Purpose,
                    Repositories = entry.Repositories,
                    ProductId = bySlug.TryGetValue(entry.Slug, out var id) ? id : null,
                }),
            ],
        });
    }
}
