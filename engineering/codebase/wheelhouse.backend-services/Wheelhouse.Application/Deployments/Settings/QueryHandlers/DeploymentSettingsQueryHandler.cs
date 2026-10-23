using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.Deployments.Settings.Queries;
using Wheelhouse.Application.Deployments.Settings.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.QueryHandlers;

/// <summary>Handles a read of a target's settings plan; no target is contacted and no secret value is read. The runner
/// prepares the release to read what it takes, so the target's product's approved services catch up with its
/// repositories' <c>deploy.yml</c> first.</summary>
public sealed class DeploymentSettingsQueryHandler(
    DeploymentSettingsService settings, Products.Services.ProductReleaseSourcesService releaseSources)
    : IQueryHandler<DeploymentSettingsQuery, AppResult<DeploySettingsDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploySettingsDto>> HandleAsync(DeploymentSettingsQuery request, CancellationToken cancellationToken)
    {
        await releaseSources.SyncTargetAsync(request.Target, cancellationToken);
        return await settings.PlanAsync(request.Target, request.Release, cancellationToken);
    }
}
