using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.CommandHandlers;

/// <summary>Handles <see cref="VaultImportCommand"/>; the audit entry carries the counts.</summary>
public sealed class VaultImportCommandHandler(SecretsVaultImporter importer)
    : ICommandHandler<VaultImportCommand, AppResult<VaultImportDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<VaultImportDto>> HandleAsync(VaultImportCommand request, CancellationToken cancellationToken)
    {
        var result = await importer.FromVaultAsync(request.Vault, cancellationToken);
        if (result is AppResult<VaultImportDto>.Success { Data: var report })
            request.Outcome.Detail = Describe(report);
        return result;
    }

    /// <summary>The import in one line of counts.</summary>
    /// <param name="report">The import.</param>
    /// <returns>The line.</returns>
    public static string Describe(VaultImportDto report) =>
        report.Namespaces + " namespaces, " + report.Secrets + " secrets, " + report.Versions + " versions, "
        + report.Verified + " verified, " + report.Skipped + " skipped, " + report.Failed + " failed";
}
