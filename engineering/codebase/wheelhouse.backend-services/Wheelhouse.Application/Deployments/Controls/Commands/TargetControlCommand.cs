using System.Text.Json;
using Wheelhouse.Application.Audit;
using Wheelhouse.Domain.Deployments.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Controls.Commands;

/// <summary>Stops, starts or restarts the release an environment runs (decision 45): <paramref name="Service"/>, or
/// the whole environment without one; a restart takes one service. A reset drops and recreates the environment's
/// database for the next deploy to rebuild. Prod needs <paramref name="Confirm"/>, its target ID typed, and a
/// <paramref name="Reason"/>.</summary>
/// <param name="Target">The target's slug.</param>
/// <param name="Operation">What to do.</param>
/// <param name="Service">The service; null for the whole environment.</param>
/// <param name="Actor">Who does it.</param>
/// <param name="Reason">The owner's one-line why, kept on the job.</param>
/// <param name="Confirm">The typed target ID.</param>
public sealed record TargetControlCommand(
    string Target, ControlOperation Operation, string? Service, string Actor, string? Reason = null, string? Confirm = null)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <summary>Gets the operation's lowercase name, as the runner and the audit trail name it.</summary>
    public string OperationName => Operation.ToString().ToLowerInvariant();

    /// <inheritdoc />
    public string AuditAction => Operation == ControlOperation.Reset
        ? "database.reset"
        : (Service is null ? "environment." : "service.") + OperationName;

    /// <inheritdoc />
    public string AuditSubject => Service is null ? Target : Target + "/" + Service;

    /// <inheritdoc />
    public string? AuditDetail => string.IsNullOrWhiteSpace(Reason) ? null : "reason: " + Reason.Trim();
}
