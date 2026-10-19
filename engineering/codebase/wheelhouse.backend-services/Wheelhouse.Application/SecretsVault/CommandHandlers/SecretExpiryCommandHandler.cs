using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.CommandHandlers;

/// <summary>Handles <see cref="SecretExpiryCommand"/>; metadata only, so it works while the vault is sealed.</summary>
public sealed class SecretExpiryCommandHandler(ITargetsRepository targets, SecretsVaultService vault, ISecretsVaultStore store)
    : ICommandHandler<SecretExpiryCommand, AppResult<bool>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<bool>> HandleAsync(SecretExpiryCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        if (target is null || !await vault.StageExpiryAsync(target.ProductId, target.Environment, request.Key, request.ExpiresAt, cancellationToken))
            return AppResult<bool>.Fail(AppErrorFactory.NotFound($"{request.Target} holds no secret {request.Key}."));
        await store.SaveAsync(cancellationToken);
        return AppResult<bool>.Ok(true);
    }
}
