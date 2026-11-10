using Wheelhouse.Application.Audit;
using Wheelhouse.Application.SecretsVault.Commands;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Removes a key's link to another product's key: the next deploy renders the key's own value again, if it has
/// one. The source keeps everything it holds. The audit names the source the link had, never a value.</summary>
/// <param name="Target">The target whose product environment the key belongs to.</param>
/// <param name="Key">The linking key.</param>
public sealed record SettingLinkRemoveCommand(string Target, string Key) : ICommand<AppResult<bool>>, IAuditedCommand
{
    /// <summary>Gets the source the removed link had, filled by the handler, for the audit detail.</summary>
    public VaultOutcome Outcome { get; } = new();

    /// <inheritdoc />
    public string AuditAction => "settings.unlink";

    /// <inheritdoc />
    public string AuditSubject => Target + "/" + Key;

    /// <inheritdoc />
    public string? AuditDetail => Outcome.Detail;
}
