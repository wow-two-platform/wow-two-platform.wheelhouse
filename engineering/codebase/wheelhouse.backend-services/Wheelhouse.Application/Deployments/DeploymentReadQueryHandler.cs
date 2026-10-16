using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles deployment inventory and outcome queries.</summary>
public sealed class DeploymentReadQueryHandler(IDeploymentGateway gateway)
    : IQueryHandler<DeploymentReadQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentReadQuery request, CancellationToken cancellationToken) =>
        await gateway.ReadAsync(request.Resource, request.Id, cancellationToken);
}
