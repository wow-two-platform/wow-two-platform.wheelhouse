using System.Text.Json;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Application.Vaults.Changes;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Administers the code-owned secrets vaults; secret values can be written but never read.</summary>
public interface IVaultGateway
{
    /// <summary>Reads vault metadata; <paramref name="vault"/> and <paramref name="ns"/> apply to the narrower resources.</summary>
    Task<AppResult<JsonElement>> ReadAsync(VaultResource resource, string? vault, string? ns, CancellationToken ct);
    /// <summary>Applies one administrative change to a vault.</summary>
    Task<AppResult<JsonElement>> ApplyAsync(string vault, VaultChange change, CancellationToken ct);
}
