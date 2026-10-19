using System.Text.Json;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Application.Vaults.Changes;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Administers the code-owned secrets vaults; the console writes secret values but never reads them. Only a
/// deploy's settings render reads values, through a short-lived token revoked straight after.</summary>
public interface IVaultGateway
{
    /// <summary>Reads the values of <paramref name="keys"/> in one namespace for a deploy's render: mints a namespace
    /// token that expires within minutes, named after <paramref name="purpose"/>, reads each key and revokes it. A key the
    /// vault does not hold is left out. Values are returned to the caller only, never logged.</summary>
    Task<AppResult<IReadOnlyDictionary<string, string>>> ReadValuesAsync(
        string vault, string ns, IReadOnlyCollection<string> keys, string purpose, CancellationToken ct) =>
        Task.FromResult(AppResult<IReadOnlyDictionary<string, string>>.Fail(
            WoW.Two.Sdk.Beta.Be.Foundation.Errors.AppErrorFactory.ExternalUnavailable("This vault gateway cannot read values.")));

    /// <summary>Reads vault metadata; <paramref name="vault"/> and <paramref name="ns"/> apply to the narrower resources.</summary>
    Task<AppResult<JsonElement>> ReadAsync(VaultResource resource, string? vault, string? ns, CancellationToken ct);
    /// <summary>Applies one administrative change to a vault.</summary>
    Task<AppResult<JsonElement>> ApplyAsync(string vault, VaultChange change, CancellationToken ct);
}
