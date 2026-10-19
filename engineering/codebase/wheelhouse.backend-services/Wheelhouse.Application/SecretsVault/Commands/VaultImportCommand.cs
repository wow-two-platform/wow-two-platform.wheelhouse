using Wheelhouse.Application.Audit;
using Wheelhouse.Application.SecretsVault.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Commands;

/// <summary>Imports an inventory vault's current values into the built-in vault over its management API.</summary>
/// <param name="Vault">The inventory vault's slug.</param>
public sealed record VaultImportCommand(string Vault) : ICommand<AppResult<VaultImportDto>>, IAuditedCommand
{
    /// <summary>Gets the counts, filled by the handler, for the audit detail.</summary>
    public VaultOutcome Outcome { get; } = new();

    /// <inheritdoc />
    public string AuditAction => "vault.import";

    /// <inheritdoc />
    public string AuditSubject => Vault;

    /// <inheritdoc />
    public string? AuditDetail => Outcome.Detail;
}
