using System.Text.RegularExpressions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Commands;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Services;
using Wheelhouse.Application.Products;
using Wheelhouse.Domain.Catalog.Enums;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Models;
using Wheelhouse.Domain.Workspaces.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Catalog.CommandHandlers;

/// <summary>Handles <see cref="CatalogImportCommand"/> over the merged hubs. Each chosen entry, in catalog order: an existing product with its
/// slug moves into the entry's workspace and takes the entry's <c>deploy</c> as its deploy mode, takes the entry's
/// repositories when they changed, and, when Wheelhouse deploys it, a derived release source for each repository that
/// has none, as a new product would (<see cref="CatalogDrift"/>); otherwise an entry Wheelhouse deploys (<c>deploy: wheelhouse</c>) with a
/// repository becomes a product — one product for all its repositories (decision 70) — whose release sources
/// <see cref="CatalogReleaseService"/> derives; any other entry is skipped with a warning, as is one whose repositories
/// share a service. Then each product the entry <c>replaces</c> retires when nothing ever deployed to it: its
/// environments, then the product. A workspace is created, last in the order, only when a product needs it. Every step
/// checks what exists first, so an interrupted import completes when run again.</summary>
public sealed class CatalogImportCommandHandler(
    CatalogReader reader,
    CatalogReleaseService releases,
    IProductsRepository products,
    IWorkspacesRepository workspaces,
    ITargetsRepository targets,
    IDeploymentRecords records)
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
                var deploy = CatalogDrift.ModeOf(entry.Deploy);
                var regrouped = await RegroupAsync(existing, entry, import, cancellationToken);
                if (existing.WorkspaceId == workspace.Id && existing.Deploy == deploy && !regrouped)
                {
                    await RetireAsync(entry, import, cancellationToken);
                    continue;
                }
                if (existing.WorkspaceId != workspace.Id)
                    import.Moved.Add(entry.Slug);
                if (existing.Deploy != deploy || regrouped)
                    import.Updated.Add(entry.Slug);
                existing.WorkspaceId = workspace.Id;
                existing.Deploy = deploy;
                await products.UpdateAsync(existing, cancellationToken);
                await RetireAsync(entry, import, cancellationToken);
                continue;
            }

            if (Skip(entry) is { } reason)
            {
                import.Warn(entry.Slug, reason);
                continue;
            }

            var derived = await releases.DeriveAllAsync(entry.Slug, entry.Repositories, cancellationToken);
            var repositories = derived.Select(item => (item.Repository, (ProductReleaseValueObject?)item.Derived.Release)).ToList();
            if (ProductRepositories.Problem(entry.Slug, repositories) is { } problem)
            {
                import.Warn(entry.Slug, problem + " No product was created.");
                continue;
            }

            await products.CreateAsync(new ProductEntity
            {
                Id = Guid.CreateVersion7(),
                Slug = entry.Slug,
                Name = Clip(entry.Name, CatalogConstants.NameLength),
                Description = Clip(entry.Purpose, CatalogConstants.DescriptionLength),
                Repository = derived[0].Repository,
                DefaultBranch = CatalogConstants.ReleaseBranch,
                WorkspaceId = (await import.WorkspaceAsync(entry.Workspace, cancellationToken)).Id,
                Release = derived[0].Derived.Release,
                Components = [.. derived.Skip(1).Select(item => new ProductComponentValueObject
                {
                    Repository = item.Repository, Release = item.Derived.Release,
                })],
                Deploy = CatalogDrift.ModeOf(entry.Deploy),
            }, cancellationToken);
            import.Created.Add(entry.Slug);
            foreach (var (_, item) in derived)
            {
                if (item.Note is { } note)
                    import.Warn(entry.Slug, note);
            }

            await RetireAsync(entry, import, cancellationToken);
        }

        return AppResult<CatalogImportResultDto>.Ok(new CatalogImportResultDto
        {
            CreatedWorkspaces = import.CreatedWorkspaces,
            CreatedProducts = import.Created,
            MovedProducts = import.Moved,
            UpdatedProducts = [.. import.Updated.Distinct(StringComparer.Ordinal)],
            RetiredProducts = import.Retired,
            Warnings = import.Warnings,
        });
    }

    /// <summary>Gives an existing product the entry's repositories when the entry lists others, or the same ones in
    /// another order, with release sources derived again; their services must stand together. With the same
    /// repositories, only one without a release source takes a derived one, as a product the catalog said CI deploys
    /// has none until its entry says Wheelhouse does; the others keep theirs. Says whether it changed.</summary>
    private async Task<bool> RegroupAsync(ProductEntity existing, CatalogEntryModel entry, Import import, CancellationToken ct)
    {
        var regroup = CatalogDrift.RepositoriesDiffer(existing, entry);
        if (!regroup && !CatalogDrift.LacksRelease(existing, entry))
            return false;

        var current = ProductRepositories.Of(existing);
        var derived = await releases.DeriveAllAsync(entry.Slug, entry.Repositories, ct);
        var taken = derived.Select((item, index) => regroup || current[index].Release is null).ToList();
        var repositories = derived
            .Select((item, index) => taken[index]
                ? (item.Repository, (ProductReleaseValueObject?)item.Derived.Release)
                : (current[index].Repository, current[index].Release))
            .ToList();
        if (ProductRepositories.Problem(entry.Slug, repositories) is { } problem)
        {
            import.Warn(entry.Slug, problem + (regroup ? " Its repositories stay as they were." : " Its release sources stay as they were."));
            return false;
        }

        existing.Repository = regroup ? repositories[0].Repository : existing.Repository;
        existing.Release = repositories[0].Item2;
        existing.Components = [.. repositories.Skip(1).Select(item => new ProductComponentValueObject
        {
            Repository = item.Repository, Release = item.Item2,
        })];
        for (var index = 0; index < derived.Count; index++)
        {
            if (taken[index] && derived[index].Derived.Note is { } note)
                import.Warn(entry.Slug, note);
        }

        await products.UpdateAsync(existing, ct);
        return true;
    }

    /// <summary>Retires each product the entry replaces that nothing ever deployed to: its environments go, then the
    /// product, with its settings, secrets and pins. One with a recorded deploy stays, with a warning; so does every one
    /// while the deploy history cannot be read.</summary>
    private async Task RetireAsync(CatalogEntryModel entry, Import import, CancellationToken ct)
    {
        if (entry.Replaces.Count == 0)
            return;

        IReadOnlyList<Wheelhouse.Application.Deployments.History.Models.DeploymentRecord>? history = null;
        foreach (var slug in entry.Replaces)
        {
            var replaced = await products.GetBySlugAsync(slug, ct);
            if (replaced is null || slug == entry.Slug)
                continue;

            if (history is null)
            {
                if (await records.ListAsync(ct) is not AppResult<IReadOnlyList<Wheelhouse.Application.Deployments.History.Models.DeploymentRecord>>.Success { Data: var read })
                {
                    import.Warn(slug, $"'{entry.Slug}' replaces it, but the deploy history could not be read, so it stays; import again to retire it.");
                    continue;
                }

                history = read;
            }

            var environments = (await targets.GetAllAsync(ct)).Where(target => target.ProductId == replaced.Id).ToList();
            var slugs = environments.Select(target => target.Slug).ToHashSet(StringComparer.Ordinal);
            if (history.Any(record => (record.TargetId is { } target && slugs.Contains(target)) || record.Product == slug))
            {
                import.Warn(slug, $"'{entry.Slug}' replaces it, but it has deploys on record, so it stays; remove its environments and the product by hand once it is torn down.");
                continue;
            }

            foreach (var environment in environments)
                await targets.DeleteAsync(environment, ct);
            await products.DeleteAsync(replaced, ct);
            import.Retired.Add(slug);
        }
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
        if (!Regex.IsMatch(entry.Repositories[0], FormatPatternConstants.GitHubRepository))
            return "Its repository is not an owner/name the inventory accepts, so no product was created.";
        return null;
    }

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    /// <summary>Tracks one import's changes and creates the workspaces its products need.</summary>
    private sealed class Import(CatalogDocument catalog, IWorkspacesRepository workspaces)
    {
        public List<string> CreatedWorkspaces { get; } = [];

        public List<string> Created { get; } = [];

        public List<string> Moved { get; } = [];

        public List<string> Updated { get; } = [];

        public List<string> Retired { get; } = [];

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
