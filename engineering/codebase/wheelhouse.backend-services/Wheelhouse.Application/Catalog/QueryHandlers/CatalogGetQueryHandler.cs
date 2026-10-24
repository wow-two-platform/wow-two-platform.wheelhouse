using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Queries;
using Wheelhouse.Application.Catalog.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Catalog.QueryHandlers;

/// <summary>Handles <see cref="CatalogGetQuery"/>: every hub's outcome, and the readable hubs' workspaces and entries; an
/// entry's product is the product with its slug, with what an import would bring in step (<see cref="CatalogDrift"/>).</summary>
public sealed class CatalogGetQueryHandler(CatalogReader reader, IProductsRepository products, IWorkspacesRepository workspaces)
    : IQueryHandler<CatalogGetQuery, AppResult<CatalogDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<CatalogDto>> HandleAsync(CatalogGetQuery request, CancellationToken cancellationToken)
    {
        var snapshot = await reader.ReadAsync(cancellationToken);
        var bySlug = (await products.GetAllAsync(cancellationToken)).ToDictionary(product => product.Slug);
        var workspaceSlugs = (await workspaces.GetAllAsync(cancellationToken)).ToDictionary(workspace => workspace.Id, workspace => workspace.Slug);
        return AppResult<CatalogDto>.Ok(new CatalogDto
        {
            Sources = [.. snapshot.Sources.Select(read => read.Source)],
            Workspaces =
            [
                .. snapshot.Document.Workspaces.Select(workspace => new CatalogWorkspaceDto
                {
                    Slug = workspace.Slug, Name = workspace.Name, Description = workspace.Description, Source = workspace.Source,
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
                    Replaces = entry.Replaces,
                    Source = entry.Source,
                    ProductId = bySlug.TryGetValue(entry.Slug, out var product) ? product.Id : null,
                    Changes = product is null ? [] : CatalogDrift.Of(product, workspaceSlugs.GetValueOrDefault(product.WorkspaceId), entry),
                }),
            ],
        });
    }
}
