using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles an explicit reconciliation of a locked target.</summary>
public sealed class DeploymentReconcileCommandHandler(IDeploymentGateway gateway)
    : ICommandHandler<DeploymentReconcileCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentReconcileCommand request, CancellationToken cancellationToken) =>
        await gateway.ReconcileAsync(request.Target, request.Job, request.Actor, cancellationToken);
}
