using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles a read-only target readiness check.</summary>
/// <remarks>The check weighs the setting keys Wheelhouse would send (stored settings and vault secrets, names only), so
/// it names a required key no layer sets.</remarks>
public sealed class DeploymentCheckQueryHandler(
    IDeploymentGateway gateway, ITargetsRepository targets, Settings.Services.DeploymentSettingsService settings)
    : IQueryHandler<DeploymentCheckQuery, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentCheckQuery request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        var keys = target is null ? null : await settings.KeysAsync(target, cancellationToken);
        return keys is null
            ? await gateway.CheckAsync(request.Target, request.Release, cancellationToken)
            : await gateway.CheckAsync(request.Target, request.Release, keys, cancellationToken);
    }
}
