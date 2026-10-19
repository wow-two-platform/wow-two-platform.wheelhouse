using Wheelhouse.Application.Audit;
using Wheelhouse.Application.SecretsVault.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Commands;

/// <summary>Imports every version from a standalone vault's database (or a restore of its dump) under that vault's
/// master key: the root key file by default, when the owner kept the same key.</summary>
/// <param name="ConnectionString">The standalone vault's database; never logged or audited.</param>
/// <param name="SourceKey">The standalone vault's master key, when it differs from the root key; cleared after use.</param>
public sealed record VaultDatabaseImportCommand(string ConnectionString, byte[]? SourceKey) : ICommand<AppResult<VaultImportDto>>, IAuditedCommand
{
    /// <summary>Gets the counts, filled by the handler, for the audit detail.</summary>
    public VaultOutcome Outcome { get; } = new();

    /// <inheritdoc />
    public string AuditAction => "vault.import";

    /// <inheritdoc />
    public string AuditSubject => "database";

    /// <inheritdoc />
    public string? AuditDetail => Outcome.Detail;

    /// <summary>Returns the command without its connection string or key.</summary>
    public override string ToString() => nameof(VaultDatabaseImportCommand);
}
