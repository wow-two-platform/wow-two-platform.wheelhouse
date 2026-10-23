using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles deployment inventory and outcome queries. Before the runner lists the release catalog, every
/// product's approved services catch up with its repositories' <c>deploy.yml</c>, so the release sets it composes take
/// a repository's new services and its first descriptor.</summary>
public sealed class DeploymentReadQueryHandler(IDeploymentGateway gateway, ProductReleaseSourcesService releaseSources)
    : IQueryHandler<DeploymentReadQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentReadQuery request, CancellationToken cancellationToken)
    {
        if (request.Resource == "releases")
            await releaseSources.SyncAllAsync(cancellationToken);
        return await gateway.ReadAsync(request.Resource, request.Id, cancellationToken);
    }
}
