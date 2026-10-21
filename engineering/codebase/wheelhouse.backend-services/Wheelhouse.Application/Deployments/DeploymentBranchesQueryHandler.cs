using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles branch listings for choosing which commits to build or deploy.</summary>
public sealed class DeploymentBranchesQueryHandler(IDeploymentGateway gateway)
    : IQueryHandler<DeploymentBranchesQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentBranchesQuery request, CancellationToken cancellationToken) =>
        await gateway.BranchesAsync(request.Product, request.Repository, cancellationToken);
}
