using Wheelhouse.Application.Audit;
using Wheelhouse.Application.SecretsVault.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Commands;

/// <summary>Restores an earlier version of a secret as its newest version; the history keeps both.</summary>
/// <param name="Target">The target whose environment holds it.</param>
/// <param name="Key">The secret's key.</param>
/// <param name="Version">The version to restore.</param>
/// <param name="Note">The owner's note.</param>
/// <param name="Actor">Who restores it.</param>
public sealed record SecretRestoreCommand(string Target, string Key, int Version, string? Note, string Actor)
    : ICommand<AppResult<SecretWritten>>, IAuditedCommand
{
    /// <summary>Gets the new version, filled by the handler, for the audit detail.</summary>
    public VaultOutcome Outcome { get; } = new();

    /// <inheritdoc />
    public string AuditAction => "settings.secret.restore";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => Key + " v" + Version + (Outcome.Detail is { } detail ? " as " + detail : "");
}
