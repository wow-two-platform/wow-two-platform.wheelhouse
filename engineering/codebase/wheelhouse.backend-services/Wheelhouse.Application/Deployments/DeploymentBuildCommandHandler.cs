using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles an explicitly requested build of one commit.</summary>
public sealed class DeploymentBuildCommandHandler(IDeploymentGateway gateway)
    : ICommandHandler<DeploymentBuildCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentBuildCommand request, CancellationToken cancellationToken) =>
        await gateway.RequestBuildAsync(request.Product, request.Commit, cancellationToken);
}
