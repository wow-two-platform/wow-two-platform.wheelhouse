using Wheelhouse.Application.Audit;
using Wheelhouse.Application.SecretsVault.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Commands;

/// <summary>Decrypts everything the built-in vault holds and counts what decrypted — the restore proof.</summary>
public sealed record VaultVerifyCommand : ICommand<AppResult<VaultVerificationDto>>, IAuditedCommand
{
    /// <summary>Gets the counts, filled by the handler, for the audit detail.</summary>
    public VaultOutcome Outcome { get; } = new();

    /// <inheritdoc />
    public string AuditAction => "vault.verify";

    /// <inheritdoc />
    public string AuditSubject => "built-in";

    /// <inheritdoc />
    public string? AuditDetail => Outcome.Detail;
}
