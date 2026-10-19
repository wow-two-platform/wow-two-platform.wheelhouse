using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Queries;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.QueryHandlers;

/// <summary>Handles <see cref="SecretHistoryQuery"/>.</summary>
public sealed class SecretHistoryQueryHandler(ITargetsRepository targets, SecretsVaultService vault)
    : IQueryHandler<SecretHistoryQuery, AppResult<SecretHistoryDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<SecretHistoryDto>> HandleAsync(SecretHistoryQuery request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        var history = target is null ? null
            : await vault.HistoryAsync(target.Slug, target.ProductId, target.Environment, request.Key, cancellationToken);
        return history is null
            ? AppResult<SecretHistoryDto>.Fail(AppErrorFactory.NotFound($"{request.Target} holds no secret {request.Key}."))
            : AppResult<SecretHistoryDto>.Ok(history);
    }
}
