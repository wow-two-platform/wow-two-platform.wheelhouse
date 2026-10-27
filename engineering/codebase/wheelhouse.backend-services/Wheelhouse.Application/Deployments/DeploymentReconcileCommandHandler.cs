using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Following.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles an explicit reconciliation of a locked target; it wakes the deployment follower, which then reads
/// the reconciled outcome.</summary>
public sealed class DeploymentReconcileCommandHandler(IDeploymentGateway gateway, DeploymentFollowSchedule following)
    : ICommandHandler<DeploymentReconcileCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentReconcileCommand request, CancellationToken cancellationToken)
    {
        var result = await gateway.ReconcileAsync(request.Target, request.Job, request.Actor, cancellationToken);
        following.Wake();
        return result;
    }
}
