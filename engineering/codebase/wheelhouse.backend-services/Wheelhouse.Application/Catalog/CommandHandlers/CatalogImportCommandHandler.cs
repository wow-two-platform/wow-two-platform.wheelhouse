using System.Text.RegularExpressions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Commands;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Services;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Domain.Catalog.Enums;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Domain.Workspaces.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Catalog.CommandHandlers;

/// <summary>Handles <see cref="CatalogImportCommand"/> over the merged hubs. Each chosen entry, in catalog order: an existing product with its
/// slug moves into the entry's workspace and takes the entry's <c>deploy</c> as its deploy mode; otherwise an entry Wheelhouse deploys (<c>deploy: wheelhouse</c>) with a
/// repository becomes a product, whose release source <see cref="CatalogReleaseService"/> derives; any other entry is
/// skipped with a warning. A workspace is created, last in the order, only when a product needs it. Every step checks
/// what exists first, so an interrupted import completes when run again.</summary>
public sealed class CatalogImportCommandHandler(
    CatalogReader reader,
    CatalogReleaseService releases,
    IProductsRepository products,
    IWorkspacesRepository workspaces)
    : ICommandHandler<CatalogImportCommand, AppResult<CatalogImportResultDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<CatalogImportResultDto>> HandleAsync(CatalogImportCommand request, CancellationToken cancellationToken)
    {
        var snapshot = await reader.ReadAsync(cancellationToken);
        var catalog = snapshot.Document;
        var unknown = request.Slugs.Where(slug => catalog.Entries.All(entry => entry.Slug != slug)).Distinct().ToList();
        if (unknown.Count > 0)
            return AppResult<CatalogImportResultDto>.Fail(AppErrorFactory.Validation(Unknown(unknown, snapshot.Sources)));

        var import = new Import(catalog, workspaces);
        foreach (var entry in catalog.Entries.Where(entry => request.Slugs.Contains(entry.Slug)))
        {
            var existing = await products.GetBySlugAsync(entry.Slug, cancellationToken);
            if (existing is not null)
            {
                var workspace = await import.WorkspaceAsync(entry.Workspace, cancellationToken);
                var deploy = ModeOf(entry.Deploy);
                if (existing.WorkspaceId == workspace.Id && existing.Deploy == deploy)
                    continue;
                if (existing.WorkspaceId != workspace.Id)
                    import.Moved.Add(entry.Slug);
                if (existing.Deploy != deploy)
                    import.Updated.Add(entry.Slug);
                existing.WorkspaceId = workspace.Id;
                existing.Deploy = deploy;
                await products.UpdateAsync(existing, cancellationToken);
                continue;
            }

            if (Skip(entry) is { } reason)
            {
                import.Warn(entry.Slug, reason);
                continue;
            }

            var repository = entry.Repositories[0];
            var derived = await releases.DeriveAsync(entry.Slug, repository, cancellationToken);
            await products.CreateAsync(new ProductEntity
            {
                Id = Guid.CreateVersion7(),
                Slug = entry.Slug,
                Name = Clip(entry.Name, CatalogConstants.NameLength),
                Description = Clip(entry.Purpose, CatalogConstants.DescriptionLength),
                Repository = repository,
                DefaultBranch = CatalogConstants.ReleaseBranch,
                WorkspaceId = (await import.WorkspaceAsync(entry.Workspace, cancellationToken)).Id,
                Release = derived.Release,
                Deploy = ModeOf(entry.Deploy),
            }, cancellationToken);
            import.Created.Add(entry.Slug);
            if (derived.Note is { } note)
                import.Warn(entry.Slug, note);
        }

        return AppResult<CatalogImportResultDto>.Ok(new CatalogImportResultDto
        {
            CreatedWorkspaces = import.CreatedWorkspaces,
            CreatedProducts = import.Created,
            MovedProducts = import.Moved,
            UpdatedProducts = import.Updated,
            Warnings = import.Warnings,
        });
    }

    /// <summary>Names the slugs no readable hub lists, and each hub that could not be read, since it may list them.</summary>
    private static string Unknown(IReadOnlyList<string> slugs, IReadOnlyList<CatalogSourceRead> sources)
    {
        var message = "The catalog has no entry " + string.Join(", ", slugs.Select(slug => $"'{slug}'")) + ".";
        var unread = sources.Where(read => read.Failure is not null).Select(read => read.Failure!.Message).ToList();
        return unread.Count == 0 ? message : message + " Hubs not listed: " + string.Join(" ", unread);
    }

    /// <summary>Says why an entry with no product yet does not become one, or <c>null</c> when it does.</summary>
    private static string? Skip(CatalogEntryModel entry)
    {
        if (entry.Deploy != CatalogDeploy.Wheelhouse)
            return $"Wheelhouse does not deploy it (deploy: {entry.Deploy.ToString().ToLowerInvariant()}), so no product was created.";
        if (entry.Repositories.Count == 0)
            return "It has no GitHub repository yet, so no product was created.";
        if (!Regex.IsMatch(entry.Repositories[0], InventoryPatternConstants.Repository))
            return "Its repository is not an owner/name the inventory accepts, so no product was created.";
        return null;
    }

    /// <summary>The deploy mode a product takes from its catalog entry's <c>deploy</c>.</summary>
    private static ProductDeployMode ModeOf(CatalogDeploy deploy) => deploy switch
    {
        CatalogDeploy.Ci => ProductDeployMode.Ci,
        CatalogDeploy.None => ProductDeployMode.None,
        _ => ProductDeployMode.Wheelhouse,
    };

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    /// <summary>Tracks one import's changes and creates the workspaces its products need.</summary>
    private sealed class Import(CatalogDocument catalog, IWorkspacesRepository workspaces)
    {
        public List<string> CreatedWorkspaces { get; } = [];

        public List<string> Created { get; } = [];

        public List<string> Moved { get; } = [];

        public List<string> Updated { get; } = [];

        public List<CatalogImportWarningDto> Warnings { get; } = [];

        public void Warn(string slug, string message) => Warnings.Add(new CatalogImportWarningDto { Slug = slug, Message = message });

        public async Task<WorkspaceEntity> WorkspaceAsync(string slug, CancellationToken ct)
        {
            if (await workspaces.GetBySlugAsync(slug, ct) is { } existing)
                return existing;

            var declared = catalog.Workspaces.Single(workspace => workspace.Slug == slug);
            var created = await workspaces.CreateAsync(new WorkspaceEntity
            {
                Id = Guid.CreateVersion7(),
                Slug = declared.Slug,
                Name = Clip(declared.Name, CatalogConstants.NameLength),
                Description = declared.Description is { } description ? Clip(description, 200) : null,
                Order = await workspaces.NextOrderAsync(ct),
            }, ct);
            CreatedWorkspaces.Add(slug);
            return created;
        }
    }
}
