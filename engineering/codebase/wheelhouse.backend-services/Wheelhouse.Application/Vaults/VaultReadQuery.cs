using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Vaults;

/// <summary>Reads vault metadata.</summary>
public sealed record VaultReadQuery(VaultResource Resource, string? Vault = null, string? Namespace = null)
    : IQuery<AppResult<JsonElement>>;
