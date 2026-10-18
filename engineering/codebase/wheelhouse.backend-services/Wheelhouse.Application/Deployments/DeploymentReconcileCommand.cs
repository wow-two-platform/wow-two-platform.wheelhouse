using System.Text.Json;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Acknowledges an interrupted or failed rollout after the owner inspected its target.</summary>
public sealed record DeploymentReconcileCommand(string Target, string Job, string Actor)
    : ICommand<AppResult<JsonElement>>, IAuditedCommand
{
    /// <inheritdoc />
    public string AuditAction => "deployment.reconcile";

    /// <inheritdoc />
    public string AuditSubject => Target;

    /// <inheritdoc />
    public string AuditDetail => "deployment " + Job;
}
