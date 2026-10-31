using Wheelhouse.Application.Audit;
using Wheelhouse.Application.SecretsVault.Commands;
using Wheelhouse.Application.SecretsVault.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Generates a new value for a secret key the release has Wheelhouse generate (<c>generate</c>), as its newest
/// version in the target's environment; earlier versions stay, and the next deploy or apply renders it. The value goes to
/// the vault only: the reply and the audit name the key and its version, never the value.</summary>
/// <param name="Target">The target whose environment holds it.</param>
/// <param name="Key">The secret's key.</param>
/// <param name="Actor">Who regenerates it.</param>
/// <param name="Note">The owner's note on the new version.</param>
/// <param name="Release">The release bundle whose declaration and image make the value; null for the one the target
/// runs.</param>
public sealed record SecretRegenerateCommand(string Target, string Key, string Actor, string? Note = null, string? Release = null)
    : ICommand<AppResult<SecretWritten>>, IAuditedCommand
{
    /// <summary>Gets the new version and how it was made, filled by the handler, for the audit detail.</summary>
    public VaultOutcome Outcome { get; } = new();

    /// <inheritdoc />
    public string AuditAction => "settings.secret.regenerate";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => Key + (Outcome.Detail is { } detail ? " " + detail : "");

    /// <summary>Returns the command without its note, which a log need not repeat.</summary>
    public override string ToString() => nameof(SecretRegenerateCommand) + " { " + Target + ": " + Key + " }";
}
