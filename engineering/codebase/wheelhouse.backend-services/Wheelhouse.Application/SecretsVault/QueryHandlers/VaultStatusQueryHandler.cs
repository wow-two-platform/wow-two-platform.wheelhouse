using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Queries;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.QueryHandlers;

/// <summary>Handles <see cref="VaultStatusQuery"/>.</summary>
public sealed class VaultStatusQueryHandler(SecretsVaultService vault, SecretsVaultSettings settings)
    : IQueryHandler<VaultStatusQuery, AppResult<VaultStatusDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<VaultStatusDto>> HandleAsync(VaultStatusQuery request, CancellationToken cancellationToken) =>
        AppResult<VaultStatusDto>.Ok(await vault.StatusAsync(settings.Mode.ToLowerInvariant(), cancellationToken));
}
