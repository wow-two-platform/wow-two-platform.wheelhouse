using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles changelog reads for a promotion.</summary>
public sealed class DeploymentChangelogQueryHandler(IDeploymentGateway gateway)
    : IQueryHandler<DeploymentChangelogQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentChangelogQuery request, CancellationToken cancellationToken) =>
        await gateway.ChangelogAsync(request.Product, request.From, request.To, cancellationToken);
}
