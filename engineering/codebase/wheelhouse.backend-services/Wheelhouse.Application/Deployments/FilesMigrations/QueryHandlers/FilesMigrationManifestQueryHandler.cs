using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.FilesMigrations.CommandHandlers;
using Wheelhouse.Application.Deployments.FilesMigrations.Models;
using Wheelhouse.Application.Deployments.FilesMigrations.Queries;
using Wheelhouse.Application.Deployments.FilesMigrations.Services;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.FilesMigrations.QueryHandlers;

/// <summary>Handles <see cref="FilesMigrationManifestQuery"/>: the sources of the manifests at the commits the dev
/// target runs, read as a migration reads them (prod, Wheelhouse's own and a target without a manifest are 409), and every
/// <c>files</c> target of its environment with its <c>app</c> site's host and aliases as https URLs.</summary>
public sealed class FilesMigrationManifestQueryHandler(
    FilesMigrationReleaseReader reader, IProductsRepository products, ITargetsRepository targets, IDeploymentRecords records)
    : IQueryHandler<FilesMigrationManifestQuery, AppResult<FilesMigrationManifestDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<FilesMigrationManifestDto>> HandleAsync(FilesMigrationManifestQuery request, CancellationToken cancellationToken)
    {
        var (target, product, refusal) = await reader.ResolveAsync(request.Target, cancellationToken);
        if (refusal is not null)
            return AppResult<FilesMigrationManifestDto>.Fail(refusal);
        if (await records.ListAsync(cancellationToken) is not AppResult<IReadOnlyList<Deployments.History.Models.DeploymentRecord>>.Success { Data: var all })
            return AppResult<FilesMigrationManifestDto>.Fail(AppErrorFactory.ExternalUnavailable("Deployment history could not be read."));

        var manifest = await reader.ManifestAsync(product!, FilesMigrationReleaseReader.Running(all, target!), cancellationToken);
        if (manifest.Failure is not null)
            return AppResult<FilesMigrationManifestDto>.Fail(manifest.Failure);

        var filesTargets = new List<FilesMigrationFilesTargetDto>();
        if (await products.GetBySlugAsync(FilesMigrationCommandHandler.FilesProduct, cancellationToken) is { } filesProduct)
            foreach (var candidate in (await targets.GetAllAsync(cancellationToken)).Where(item => item.ProductId == filesProduct.Id && item.Environment == target!.Environment))
                if (candidate.Sites.FirstOrDefault(site => site.Site == "app") is { } site)
                    filesTargets.Add(new FilesMigrationFilesTargetDto(candidate.Slug,
                        [.. new[] { site.Host }.Concat(site.Aliases).Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.OrdinalIgnoreCase)
                            .Select(name => "https://" + name + "/")]));
        filesTargets.Sort((left, right) => string.CompareOrdinal(left.Target, right.Target));

        return AppResult<FilesMigrationManifestDto>.Ok(new FilesMigrationManifestDto(
            manifest.Tenant!,
            [.. manifest.Sources.Select(source => new FilesMigrationSourceDto(source.Id, source.Kind, source.Wave, source.Repository))],
            filesTargets));
    }
}
