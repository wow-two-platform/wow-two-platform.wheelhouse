using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Parsers;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Catalog.Services;

/// <summary>Reads the wow2 catalog from the repository <see cref="CatalogSettings"/> names, at the newest commit of its
/// ref (the head is read again on every call, so a pushed catalog shows at once; a commit's file stays cached).</summary>
public sealed class CatalogReader(IRepositoryFileSource files, CatalogSettings settings)
{
    /// <summary>Reads and parses the catalog.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The catalog; 503 when GitHub cannot serve it, 404 when the file is missing, and 422 naming the line
    /// and field when it is malformed.</returns>
    public async Task<AppResult<CatalogSnapshot>> ReadAsync(CancellationToken ct)
    {
        var where = $"{settings.Repository}@{settings.Ref}";
        var commit = await files.HeadAsync(settings.Repository, settings.Ref, fresh: true, ct);
        if (commit is null)
            return AppResult<CatalogSnapshot>.Fail(AppErrorFactory.ExternalUnavailable(
                $"{where} could not be read from GitHub; check the sign-in's repository access and try again."));

        var file = await files.ReadAsync(settings.Repository, commit, settings.Path, CatalogConstants.MaxCatalogBytes, ct);
        switch (file.Status)
        {
            case RepositoryFileStatus.Missing:
                return AppResult<CatalogSnapshot>.Fail(AppErrorFactory.FileNotFound($"{where} has no {settings.Path}."));
            case RepositoryFileStatus.TooLarge:
                return AppResult<CatalogSnapshot>.Fail(AppErrorFactory.BusinessRule(
                    $"{settings.Path} in {where} is larger than {CatalogConstants.MaxCatalogBytes / 1024} KiB."));
            case RepositoryFileStatus.Unreadable:
                return AppResult<CatalogSnapshot>.Fail(AppErrorFactory.ExternalUnavailable(
                    $"{settings.Path} in {where} could not be read from GitHub; try again."));
        }

        var parsed = CatalogParser.Parse(file.Content!, settings.Path);
        if (parsed.Value is null)
            return AppResult<CatalogSnapshot>.Fail(AppErrorFactory.BusinessRule($"{settings.Path} {parsed.Problem}"));

        return AppResult<CatalogSnapshot>.Ok(new CatalogSnapshot(
            new CatalogSourceDto { Repository = settings.Repository, Path = settings.Path, Ref = settings.Ref, Commit = commit },
            parsed.Value));
    }
}
