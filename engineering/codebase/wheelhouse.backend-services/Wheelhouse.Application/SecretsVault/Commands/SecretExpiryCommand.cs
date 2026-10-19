using System.Globalization;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.SecretsVault.Commands;

/// <summary>Sets or clears a secret's expiry; warnings show 30 and 7 days before it lapses.</summary>
/// <param name="Target">The target whose environment holds it.</param>
/// <param name="Key">The secret's key.</param>
/// <param name="ExpiresAt">When it lapses; null clears the expiry.</param>
public sealed record SecretExpiryCommand(string Target, string Key, DateTimeOffset? ExpiresAt) : ICommand<AppResult<bool>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "settings.secret.expiry";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => Key + (ExpiresAt is { } at
        ? " expires " + at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : " has no expiry");
}
