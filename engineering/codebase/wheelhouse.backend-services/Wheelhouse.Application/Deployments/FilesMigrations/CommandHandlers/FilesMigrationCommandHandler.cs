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
    FilesMigrationReleaseReader reader, IEnvironmentSecrets secrets, IFilesImportClient filesClient, DeploymentFollowSchedule following)
    : ICommandHandler<FilesMigrationCommand, AppResult<JsonElement>>
{
    /// <summary>Holds the Files product's slug.</summary>
    public const string FilesProduct = "files";

    /// <summary>Holds the services whose image is the mover, newest name first: Files' service is named <c>fss</c> since
    /// the FSS rename (2026-11-10) and <c>files</c> before it; the first one the running release has is taken.</summary>
    public static readonly IReadOnlyList<string> FilesServices = ["fss", "files"];

    /// <summary>Holds the vault key of the Files operator API key.</summary>
    public const string OperatorKey = "Files:Operator:ApiKey";

    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(FilesMigrationCommand request, CancellationToken cancellationToken)
    {
        var (resolved, owner, refusal) = await reader.ResolveAsync(request.Target, cancellationToken);
        if (refusal is not null)
            return Fail(refusal);
        var target = resolved!;
        var product = owner!;

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

        var manifest = await reader.ManifestAsync(product, FilesMigrationReleaseReader.Running(all, target), cancellationToken);
        if (manifest.Failure is not null)
            return Fail(manifest.Failure);
        var picked = FilesMigrationManifestParser.Pick(manifest.Sources, request.Wave, request.Sources);
        if (picked.Count == 0)
            return Fail(AppErrorFactory.Validation("No source matches the wave or ids asked for."));
        request.Picked = [.. picked.Select(source => source.Id)];

        var services = FilesMigrationReleaseReader.Running(all, filesTarget)?.Services;
        var image = FilesServices.Select(name => services?.GetValueOrDefault(name)?.Image).FirstOrDefault(found => !string.IsNullOrEmpty(found));
        if (string.IsNullOrEmpty(image))
            return Fail(AppErrorFactory.Conflict($"{filesTarget.Slug} runs no {string.Join(" or ", FilesServices)} image yet; deploy it first."));

        var import = request.Import;
        string? token = null;
        DateTimeOffset? expiresAt = null;
        if (request.Step != "plan")
        {
            if (await filesClient.HealthAsync(request.FilesUrl, cancellationToken) is AppResult<bool>.Failure unhealthy)
                return Fail(unhealthy.Error);
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
            expiresAt = opened.ExpiresAt;
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
            // The runner starts no source within ten minutes of the token's expiry, and names an expired token rather
            // than a refusal; the same step run again mints a fresh one and resumes.
            tokenExpiresAt = expiresAt,
            sources = picked.Select(source => source.Fields).ToList(),
        });
        var result = await gateway.FilesMigrationAsync(target.Slug, request.Actor, payload, cancellationToken);
        if (result is not AppResult<JsonElement>.Failure { Error.Type: AppErrorType.Conflict })
            following.Wake();
        return result;
    }

    private static AppResult<JsonElement> Fail(AppError error) => AppResult<JsonElement>.Fail(error);
}
