using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Queries;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.QueryHandlers;

/// <summary>Handles <see cref="VaultStatusQuery"/>, with the root key's fingerprint and the last of each of its checks.</summary>
public sealed class VaultStatusQueryHandler(
    SecretsVaultService vault, SecretsVaultSettings settings, RootKeyChecks rootKey, RootKeyFileMonitor fileChecks)
    : IQueryHandler<VaultStatusQuery, AppResult<VaultStatusDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<VaultStatusDto>> HandleAsync(VaultStatusQuery request, CancellationToken cancellationToken) =>
        AppResult<VaultStatusDto>.Ok(await vault.StatusAsync(settings.Mode.ToLowerInvariant(), cancellationToken) with
        {
            OfflineCopy = await rootKey.OfflineCopyAsync(cancellationToken), KeyFileCheck = fileChecks.Last,
        });
}
