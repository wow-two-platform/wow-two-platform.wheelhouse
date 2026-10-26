using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Data.Sessions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.CommandHandlers;

/// <summary>Handles <see cref="SecretRestoreCommand"/>: the earlier value becomes a new version, origin noted.</summary>
public sealed class SecretRestoreCommandHandler(ITargetsRepository targets, SecretsVaultService vault, IDataSession session)
    : ICommandHandler<SecretRestoreCommand, AppResult<SecretWritten>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<SecretWritten>> HandleAsync(SecretRestoreCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        if (target is null)
            return AppResult<SecretWritten>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));
        // Completing the unit saves what the vault staged and commits; leaving it without completing keeps nothing.
        await using var unit = await session.BeginAsync(cancellationToken);
        var result = await vault.StageRestoreAsync(target.ProductId, target.Environment, request.Key, request.Version, request.Note,
            request.Actor, cancellationToken);
        if (result is not AppResult<SecretWritten>.Success { Data: var written })
            return result;
        await unit.CompleteAsync(cancellationToken);
        request.Outcome.Detail = "v" + written.Version;
        return result;
    }
}
