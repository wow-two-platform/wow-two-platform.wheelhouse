using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Controls.Commands;
using Wheelhouse.Application.Deployments.Following.Services;
using Wheelhouse.Application.Deployments.FilesMigrations.Commands;
using Wheelhouse.Application.Deployments.FilesMigrations.Models;
using Wheelhouse.Application.Deployments.FilesMigrations.Services;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.Products;
using Wheelhouse.Application.Targets.Services;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.FilesMigrations.CommandHandlers;

/// <summary>Handles <see cref="FilesMigrationCommand"/>: dev targets only (prod and Wheelhouse's own are 409), a Files
/// target of the same environment, a URL on that target's <c>app</c> site, the sources of the manifests at the commits
/// the target runs, and — for a push or a verify — an import token from Files, opened or minted with the operator key
/// read from Wheelhouse's own vault. The runner receives the whole payload on its standard input; the token goes nowhere
/// else. A Files error is a 502 naming the status.</summary>
public sealed class FilesMigrationCommandHandler(
    IDeploymentGateway gateway, ITargetsRepository targets, IProductsRepository products, IDeploymentRecords records,
    IGitHubRepositoryReader files, IEnvironmentSecrets secrets, IFilesImportClient filesClient, DeploymentFollowSchedule following)
    : ICommandHandler<FilesMigrationCommand, AppResult<JsonElement>>
{
    /// <summary>Holds the Files product's slug.</summary>
    public const string FilesProduct = "files";

    /// <summary>Holds the service whose image is the mover.</summary>
    public const string FilesService = "files";

    /// <summary>Holds the vault key of the Files operator API key.</summary>
    public const string OperatorKey = "Files:Operator:ApiKey";

    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(FilesMigrationCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        if (target is null)
            return Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));
        var product = await products.GetByIdAsync(target.ProductId, cancellationToken);
        if (product is null)
            return Fail(AppErrorFactory.NotFound($"Target '{request.Target}' has no product."));
        if (SelfTargets.IsSelf(product.Slug))
            return Fail(AppErrorFactory.Conflict($"{target.Slug} runs Wheelhouse itself; its files never move from the console."));
        if (target.Environment == DeploymentEnvironment.Prod)
            return Fail(AppErrorFactory.Conflict("Files migrations run on dev targets; prod comes with its own placement."));

        var filesTarget = await targets.GetBySlugAsync(request.FilesTarget, cancellationToken);
        var filesProduct = filesTarget is null ? null : await products.GetByIdAsync(filesTarget.ProductId, cancellationToken);
        if (filesTarget is null || filesProduct is null || filesProduct.Slug != FilesProduct || filesTarget.Environment != target.Environment)
            return Fail(AppErrorFactory.Validation($"'FilesTarget' must be a target of the {FilesProduct} product in {target.Slug}'s environment."));
        var host = new Uri(request.FilesUrl).Host;
        var site = filesTarget.Sites.FirstOrDefault(item => item.Site == "app");
        if (site is null || !new[] { site.Host }.Concat(site.Aliases).Any(name => string.Equals(name, host, StringComparison.OrdinalIgnoreCase)))
            return Fail(AppErrorFactory.Validation($"'FilesUrl' must be on {filesTarget.Slug}'s app site host or one of its aliases."));

        var history = await records.ListAsync(cancellationToken);
        if (history is not AppResult<IReadOnlyList<Deployments.History.Models.DeploymentRecord>>.Success { Data: var all })
            return Fail(AppErrorFactory.ExternalUnavailable("Deployment history could not be read."));

        var manifest = await ManifestAsync(product, Running(all, target), cancellationToken);
        if (manifest.Failure is not null)
            return Fail(manifest.Failure);
        var picked = FilesMigrationManifestParser.Pick(manifest.Sources, request.Wave, request.Sources);
        if (picked.Count == 0)
            return Fail(AppErrorFactory.Validation("No source matches the wave or ids asked for."));
        request.Picked = [.. picked.Select(source => source.Id)];

        var image = Running(all, filesTarget)?.Services?.GetValueOrDefault(FilesService)?.Image;
        if (string.IsNullOrEmpty(image))
            return Fail(AppErrorFactory.Conflict($"{filesTarget.Slug} runs no {FilesService} image yet; deploy it first."));

        var import = request.Import;
        string? token = null;
        if (request.Step != "plan")
        {
            var read = await secrets.ReadAsync(filesTarget, filesProduct, [OperatorKey], "files migration " + target.Slug, cancellationToken);
            if (read is not AppResult<IReadOnlyDictionary<string, Wheelhouse.Application.SecretsVault.Models.RenderedSecret>>.Success { Data: var values }
                || !values.TryGetValue(OperatorKey, out var key) || string.IsNullOrEmpty(key.Value))
                return Fail(AppErrorFactory.Conflict($"{filesTarget.Slug}'s vault holds no {OperatorKey}."));

            var answer = import is null
                ? await filesClient.OpenAsync(request.FilesUrl, key.Value, manifest.Tenant!, request.Picked, cancellationToken)
                : await filesClient.TokenAsync(request.FilesUrl, key.Value, import, cancellationToken);
            if (answer is AppResult<FilesImport>.Failure failure)
                return Fail(failure.Error);
            var opened = ((AppResult<FilesImport>.Success)answer).Data;
            token = opened.Token;
            if (import is null)
            {
                import = opened.Id;
                request.Opened = import;
            }
        }

        var payload = JsonSerializer.SerializeToElement(new
        {
            step = request.Step,
            image,
            files = request.FilesUrl,
            import = request.Step == "plan" ? null : import,
            token,
            sources = picked.Select(source => source.Fields).ToList(),
        });
        var result = await gateway.FilesMigrationAsync(target.Slug, request.Actor, payload, cancellationToken);
        if (result is not AppResult<JsonElement>.Failure { Error.Type: AppErrorType.Conflict })
            following.Wake();
        return result;
    }

    /// <summary>The latest successful deploy of a target; control records are no deploy.</summary>
    private static Deployments.History.Models.DeploymentRecord? Running(
        IReadOnlyList<Deployments.History.Models.DeploymentRecord> all, TargetEntity target) =>
        all.FirstOrDefault(record => record.TargetId == target.Slug && record.Status == "succeeded" && record.BundleId is not null
            && record.Operation is null);

    /// <summary>Reads each repository's manifest at the commit the target runs; a repository without the file adds
    /// nothing.</summary>
    private async Task<(string? Tenant, IReadOnlyList<FilesMigrationSource> Sources, AppError? Failure)> ManifestAsync(
        Wheelhouse.Domain.Products.Entities.ProductEntity product, Deployments.History.Models.DeploymentRecord? running, CancellationToken ct)
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
                sources.Add(source);
            }
        }

        return tenant is null
            ? (null, [], AppErrorFactory.Conflict("The product has no migration manifest at the commit the target runs."))
            : (tenant, sources, null);
    }

    private static AppResult<JsonElement> Fail(AppError error) => AppResult<JsonElement>.Fail(error);
}
