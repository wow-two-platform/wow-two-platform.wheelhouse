using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.SelfDeploy.Constants;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.SelfDeploy.Interceptors;

/// <summary>Guards deploys of Wheelhouse itself before the deploy handler runs. Its targets are never a fleet's step;
/// its prod is promoted only by the owner — never by automation — with the target ID typed and a reason, and never while
/// a fleet runs, since the console restarts. The runner refuses the same, so a console without this guard is still
/// safe; here the refusal comes first and is audited.</summary>
public sealed class SelfDeploymentInterceptor(
    ITargetsRepository targets, IProductsRepository products, IFleetRolloutsRepository fleets)
    : IRequestInterceptor<DeploymentStartCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(
        DeploymentStartCommand request, RequestHandlerDelegate<AppResult<JsonElement>> nextStep,
        CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        if (target is null || (await products.GetByIdAsync(target.ProductId, cancellationToken))?.Slug != SelfProduct.Slug)
            return await nextStep();
        var fleetRunning = target.Environment == DeploymentEnvironment.Prod && request.Fleet is null
            && (await fleets.GetRunningIdsAsync(cancellationToken)).Count > 0;
        return Refusal(request, target.Environment, fleetRunning) is { } refusal
            ? AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal))
            : await nextStep();
    }

    /// <summary>Why a deploy of one of Wheelhouse's own targets is refused, or null when it may go.</summary>
    /// <param name="request">The deploy.</param>
    /// <param name="environment">The target's environment.</param>
    /// <param name="fleetRunning">Whether a fleet rollout is running.</param>
    /// <returns>The refusal's display-safe text, or null.</returns>
    public static string? Refusal(DeploymentStartCommand request, DeploymentEnvironment environment, bool fleetRunning)
    {
        if (request.Fleet is not null)
            return "Wheelhouse's own targets are never part of a fleet; promote it on its own.";
        if (environment != DeploymentEnvironment.Prod)
            return null;
        if (request.Cause is not null)
            return "Wheelhouse prod is promoted only by the owner; the automatic deploy was skipped.";
        if (request.Confirm != request.Target)
            return $"Type {request.Target} to promote Wheelhouse prod.";
        if (string.IsNullOrWhiteSpace(request.Reason))
            return "Give a reason to promote Wheelhouse prod.";
        return fleetRunning
            ? "A fleet rollout is running; promote Wheelhouse prod after it ends, since the console restarts."
            : null;
    }
}
