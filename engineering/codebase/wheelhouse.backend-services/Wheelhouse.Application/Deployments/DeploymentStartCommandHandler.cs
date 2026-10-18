using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Freezes.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles a deploy. A freeze covering the target refuses an automatic deploy outright and an owner's deploy
/// unless it overrides the freeze with a reason; the runner's own gates follow. A target the inventory lacks is left to
/// the runner, which refuses it.</summary>
public sealed class DeploymentStartCommandHandler(
    IDeploymentGateway gateway, ITargetsRepository targets, IProductsRepository products, DeployFreezeService freezes)
    : ICommandHandler<DeploymentStartCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentStartCommand request, CancellationToken cancellationToken)
    {
        if (await targets.GetBySlugAsync(request.Target, cancellationToken) is { } target
            && await freezes.CoveringAsync(target.ProductId, target.Environment, cancellationToken) is { } freeze)
        {
            var slug = freeze.ProductId is { } id ? (await products.GetByIdAsync(id, cancellationToken))?.Slug : null;
            var covers = DeployFreezeService.Map(freeze, slug).Covers;
            if (request.Cause is not null)
                return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(
                    $"Deploys to {request.Target} are frozen ({covers}); the automatic deploy was skipped."));
            if (!request.OverrideFreeze)
                return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(
                    $"Deploys to {request.Target} are frozen ({covers}). Override the freeze with a reason to deploy anyway."));
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        return await gateway.StartAsync(request.Target, request.Release, request.Actor, request.Confirm, request.SkipDevPass,
            reason, cancellationToken);
    }
}
