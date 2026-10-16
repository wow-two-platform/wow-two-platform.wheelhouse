using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Vaults;

/// <summary>Handles a vault change and records who made it; the vault itself sees only Wheelhouse's identity.</summary>
public sealed class VaultChangeCommandHandler(IVaultGateway gateway, ILogger<VaultChangeCommandHandler> logger)
    : ICommandHandler<VaultChangeCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(VaultChangeCommand request, CancellationToken cancellationToken)
    {
        var result = await gateway.ApplyAsync(request.Vault, request.Change, cancellationToken);
        logger.LogInformation("Vault change by {Actor} on {Vault}: {Change} ({Outcome})",
            request.Actor, request.Vault, request.Change.Describe(), result.IsSuccess ? "applied" : "refused");
        return result;
    }
}
