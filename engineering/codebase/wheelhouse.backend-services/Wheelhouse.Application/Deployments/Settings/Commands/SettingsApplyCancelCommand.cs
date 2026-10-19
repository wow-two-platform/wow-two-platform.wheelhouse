using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Drops a target's scheduled settings apply.</summary>
/// <param name="Target">The target's slug.</param>
public sealed record SettingsApplyCancelCommand(string Target) : ICommand<AppResult<bool>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "settings.apply.cancel";

    /// <inheritdoc />
    public string AuditSubject => Target;
}
