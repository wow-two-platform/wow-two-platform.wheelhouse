using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles commit listings for choosing what to build or deploy.</summary>
public sealed class DeploymentCommitsQueryHandler(IDeploymentGateway gateway)
    : IQueryHandler<DeploymentCommitsQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentCommitsQuery request, CancellationToken cancellationToken) =>
        await gateway.CommitsAsync(request.Product, request.Branch, request.Repository, cancellationToken);
}
