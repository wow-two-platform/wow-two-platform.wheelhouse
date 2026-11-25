using Wheelhouse.Application;
using Wheelhouse.Application.Deployments;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments.QueryHandlers;

/// <summary>Handles <see cref="DeploymentLogsQuery"/>.</summary>
public sealed class DeploymentLogsQueryHandler(IDeploymentGateway gateway)
    : IQueryHandler<DeploymentLogsQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentLogsQuery request, CancellationToken cancellationToken) =>
        await gateway.LogsAsync(request.Target, request.Service, request.Tail, cancellationToken);
}
