using System.Globalization;
using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Settings.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Commands;

/// <summary>Applies a target's edited settings and secrets: now, as a deploy of its running release, or at
/// <paramref name="At"/>, replacing an apply it had scheduled.</summary>
/// <param name="Target">The target's slug.</param>
/// <param name="At">When to apply; null applies now.</param>
/// <param name="Actor">Who applies.</param>
/// <param name="Reason">The owner's one-line why, kept on the deploy.</param>
/// <param name="Confirm">The typed target ID, where the target asks for one.</param>
public sealed record SettingsApplyCommand(string Target, DateTimeOffset? At, string Actor, string? Reason = null, string? Confirm = null)
    : ICommand<AppResult<SettingsApplyDto>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "settings.apply";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => (At is { } at ? "at " + at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : "now")
        + (string.IsNullOrWhiteSpace(Reason) ? "" : ", reason: " + Reason.Trim());
}
