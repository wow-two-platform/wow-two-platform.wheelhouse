using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.CommandHandlers;

/// <summary>Handles <see cref="SecretRestoreCommand"/>: the earlier value becomes a new version, origin noted.</summary>
public sealed class SecretRestoreCommandHandler(
    ITargetsRepository targets, SecretsVaultService vault, ISecretsVaultStore store, ITransactions transactions)
    : ICommandHandler<SecretRestoreCommand, AppResult<SecretWritten>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<SecretWritten>> HandleAsync(SecretRestoreCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        if (target is null)
            return AppResult<SecretWritten>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var result = await vault.StageRestoreAsync(target.ProductId, target.Environment, request.Key, request.Version, request.Note,
            request.Actor, cancellationToken);
        if (result is not AppResult<SecretWritten>.Success { Data: var written })
            return result;
        await store.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        request.Outcome.Detail = "v" + written.Version;
        return result;
    }
}
