using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.FilesMigrations.Models;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Products;
using Wheelhouse.Application.Targets.Services;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

namespace Wheelhouse.Application.Deployments.FilesMigrations.Services;

/// <summary>Reads what a files migration starts from — the dev target, its product and the manifests of the release it
/// runs — for the migration itself and for the console's manifest read, so both refuse alike.</summary>
public sealed class FilesMigrationReleaseReader(
    ITargetsRepository targets, IProductsRepository products, IGitHubRepositoryReader files)
{
    /// <summary>Resolves the target and its product; prod and Wheelhouse's own are refused (409).</summary>
    /// <param name="slug">The target's slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The target and its product, or the refusal.</returns>
    public async Task<(TargetEntity? Target, ProductEntity? Product, AppError? Failure)> ResolveAsync(string slug, CancellationToken ct)
    {
        var target = await targets.GetBySlugAsync(slug, ct);
        if (target is null)
            return (null, null, AppErrorFactory.NotFound($"Target '{slug}' was not found."));
        var product = await products.GetByIdAsync(target.ProductId, ct);
        if (product is null)
            return (null, null, AppErrorFactory.NotFound($"Target '{slug}' has no product."));
        if (SelfTargets.IsSelf(product.Slug))
            return (null, null, AppErrorFactory.Conflict($"{target.Slug} runs Wheelhouse itself; its files never move from the console."));
        if (target.Environment == DeploymentEnvironment.Prod)
            return (null, null, AppErrorFactory.Conflict("Files migrations run on dev targets; prod comes with its own placement."));
        return (target, product, null);
    }

    /// <summary>The latest successful deploy of a target; control records are no deploy.</summary>
    /// <param name="all">The deployment history, newest first.</param>
    /// <param name="target">The target.</param>
    /// <returns>The record, or null when the target runs no release.</returns>
    public static DeploymentRecord? Running(IReadOnlyList<DeploymentRecord> all, TargetEntity target) =>
        all.FirstOrDefault(record => record.TargetId == target.Slug && record.Status == "succeeded" && record.BundleId is not null
            && record.Operation is null);

    /// <summary>Reads each repository's manifest at the commit the target runs; a repository without the file adds
    /// nothing. Each source names the repository that lists it.</summary>
    /// <param name="product">The target's product.</param>
    /// <param name="running">The deploy the target runs.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The tenant and sources, or the refusal (409).</returns>
    public async Task<(string? Tenant, IReadOnlyList<FilesMigrationSource> Sources, AppError? Failure)> ManifestAsync(
        ProductEntity product, DeploymentRecord? running, CancellationToken ct)
    {
        if (running is null)
            return (null, [], AppErrorFactory.Conflict("The target runs no release yet; deploy it first."));
        var commits = running.Components is { Count: > 0 } components
            ? components.Where(item => item.Repository is not null && item.SourceCommit is not null)
                .Select(item => (Repository: item.Repository!, Commit: item.SourceCommit!)).ToList()
            : running.SourceCommit is { } head ? [(ProductRepositories.Of(product)[0].Repository, head)] : [];
        if (commits.Count == 0)
            return (null, [], AppErrorFactory.Conflict("The release the target runs names no source commit."));

        string? tenant = null;
        var sources = new List<FilesMigrationSource>();
        foreach (var (repository, commit) in commits)
        {
            var file = await files.ReadAsync(repository, commit, FilesMigrationManifestParser.Path, FilesMigrationManifestParser.MaxBytes, ct);
            if (file.Outcome == FileRead.NotFound)
                continue;
            if (file.Outcome != FileRead.Found)
                return (null, [], AppErrorFactory.Conflict($"{FilesMigrationManifestParser.Path} in {repository} could not be read."));
            var parsed = FilesMigrationManifestParser.Parse(file.Content!);
            if (parsed.Value is not { } manifest)
                return (null, [], AppErrorFactory.Conflict($"{FilesMigrationManifestParser.Path} in {repository} {parsed.Problem}"));
            if (tenant is not null && tenant != manifest.Tenant)
                return (null, [], AppErrorFactory.Conflict($"{repository}'s manifest names another tenant than the product's other repositories."));
            tenant = manifest.Tenant;
            foreach (var source in manifest.Sources)
            {
                if (sources.Any(known => known.Id == source.Id))
                    return (null, [], AppErrorFactory.Conflict($"Source '{source.Id}' is in more than one repository's manifest."));
                sources.Add(source with { Repository = repository });
            }
        }

        return tenant is null
            ? (null, [], AppErrorFactory.Conflict("The product has no migration manifest at the commit the target runs."))
            : (tenant, sources, null);
    }
}
