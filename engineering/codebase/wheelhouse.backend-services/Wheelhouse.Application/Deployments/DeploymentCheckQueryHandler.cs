using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles a read-only target readiness check.</summary>
public sealed class DeploymentCheckQueryHandler(IDeploymentGateway gateway)
    : IQueryHandler<DeploymentCheckQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentCheckQuery request, CancellationToken cancellationToken) =>
        await gateway.CheckAsync(request.Target, request.Release, cancellationToken);
}
