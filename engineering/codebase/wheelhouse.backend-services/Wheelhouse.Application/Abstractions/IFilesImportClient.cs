using Wheelhouse.Application.Deployments.FilesMigrations.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the calls Wheelhouse makes to a Files host's import API, with the operator key. The contract lives
/// in one implementation. A Files error is an <c>ExternalUnavailable</c> failure naming the status, never the body.</summary>
public interface IFilesImportClient
{
    /// <summary>Opens an import for <paramref name="tenant"/>'s <paramref name="sources"/>: <c>POST {url}imports</c>.</summary>
    /// <param name="filesUrl">The Files host's https URL, ending with <c>/</c>.</param>
    /// <param name="operatorKey">The vault value of <c>Files:Operator:ApiKey</c>.</param>
    /// <param name="tenant">The manifest's tenant.</param>
    /// <param name="sources">The picked source ids.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The import id and its token, or the failure.</returns>
    Task<AppResult<FilesImport>> OpenAsync(string filesUrl, string operatorKey, string tenant, IReadOnlyList<string> sources, CancellationToken ct);

    /// <summary>Mints a fresh token for an import: <c>POST {url}imports/{id}/tokens</c>.</summary>
    /// <param name="filesUrl">The Files host's https URL, ending with <c>/</c>.</param>
    /// <param name="operatorKey">The vault value of <c>Files:Operator:ApiKey</c>.</param>
    /// <param name="import">The import's id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The token, or the failure.</returns>
    Task<AppResult<FilesImport>> TokenAsync(string filesUrl, string operatorKey, string import, CancellationToken ct);

    /// <summary>Checks the Files host answers before a push or verify is submitted: <c>GET {url}health</c> must answer 200
    /// within 5 seconds. The call carries no key.</summary>
    /// <param name="filesUrl">The Files host's https URL, ending with <c>/</c>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A success when healthy, else a <c>Conflict</c> failure: <c>Files at {host} is not healthy ({status or
    /// timeout})</c>.</returns>
    Task<AppResult<bool>> HealthAsync(string filesUrl, CancellationToken ct);
}
