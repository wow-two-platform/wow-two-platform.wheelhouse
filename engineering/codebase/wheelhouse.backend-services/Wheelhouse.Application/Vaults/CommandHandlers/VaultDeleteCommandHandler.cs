using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Vaults.Commands;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Vaults.CommandHandlers;

/// <summary>Handles <see cref="VaultDeleteCommand"/>.</summary>
public sealed class VaultDeleteCommandHandler(IVaultsRepository vaults)
    : ICommandHandler<VaultDeleteCommand, AppResult<Unit>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<Unit>> HandleAsync(VaultDeleteCommand request, CancellationToken cancellationToken)
    {
        var vault = await vaults.GetBySlugAsync(request.Slug, cancellationToken);
        if (vault is null)
            return AppResult<Unit>.Fail(AppErrorFactory.NotFound($"Vault '{request.Slug}' was not found."));

        await vaults.DeleteAsync(vault, cancellationToken);
        return AppResult<Unit>.Ok(default);
    }
}
