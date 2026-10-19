using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.CommandHandlers;

/// <summary>Handles <see cref="VaultDatabaseImportCommand"/>; the audit entry carries the counts.</summary>
public sealed class VaultDatabaseImportCommandHandler(SecretsVaultImporter importer)
    : ICommandHandler<VaultDatabaseImportCommand, AppResult<VaultImportDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<VaultImportDto>> HandleAsync(VaultDatabaseImportCommand request, CancellationToken cancellationToken)
    {
        var result = await importer.FromDatabaseAsync(request.ConnectionString, request.SourceKey, cancellationToken);
        if (result is AppResult<VaultImportDto>.Success { Data: var report })
            request.Outcome.Detail = VaultImportCommandHandler.Describe(report);
        return result;
    }
}
