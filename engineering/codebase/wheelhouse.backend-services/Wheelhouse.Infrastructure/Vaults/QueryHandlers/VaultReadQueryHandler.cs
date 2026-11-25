using Wheelhouse.Application;
using Wheelhouse.Application.Vaults;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Vaults.QueryHandlers;

/// <summary>Handles vault metadata reads.</summary>
public sealed class VaultReadQueryHandler(IVaultGateway gateway) : IQueryHandler<VaultReadQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(VaultReadQuery request, CancellationToken cancellationToken) =>
        await gateway.ReadAsync(request.Resource, request.Vault, request.Namespace, cancellationToken);
}
