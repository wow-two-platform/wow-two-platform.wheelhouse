using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles an explicitly requested deployment.</summary>
public sealed class DeploymentStartCommandHandler(IDeploymentGateway gateway)
    : ICommandHandler<DeploymentStartCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentStartCommand request, CancellationToken cancellationToken) =>
        await gateway.StartAsync(request.Target, request.Release, request.Actor, request.Confirm, request.SkipTestPass, cancellationToken);
}
