using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Controls.Commands;
using Wheelhouse.Application.Deployments.Following.Services;
using Wheelhouse.Application.Targets.Services;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Controls.CommandHandlers;

/// <summary>Handles <see cref="TargetControlCommand"/>: Wheelhouse's own environments are refused (the console would
/// stop itself, or lose its own data on a reset; <c>self-deploy.py</c> and a promotion replace them), prod needs its
/// target ID typed and a reason, and a target a running fleet still has to promote waits for it. The runner refuses a control while a rollout or another
/// control runs there, and every deploy while anything stays stopped. A submitted control wakes the deployment
/// follower.</summary>
public sealed class TargetControlCommandHandler(
    IDeploymentGateway gateway, ITargetsRepository targets, IProductsRepository products, IFleetRolloutsRepository fleets,
    DeploymentFollowSchedule following)
    : ICommandHandler<TargetControlCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(TargetControlCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        if (target is null)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));
        var product = await products.GetByIdAsync(target.ProductId, cancellationToken);
        if (product is not null && SelfTargets.IsSelf(product.Slug))
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(request.Operation == ControlOperation.Reset
                ? $"{target.Slug} runs Wheelhouse itself; its database is never reset from the console."
                : $"{target.Slug} runs Wheelhouse itself; its services are never stopped or restarted from the console."));

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (target.Environment == DeploymentEnvironment.Prod)
        {
            if (!string.Equals(request.Confirm, target.Slug, StringComparison.Ordinal))
                return AppResult<JsonElement>.Fail(AppErrorFactory.Validation($"Type {target.Slug} to {request.OperationName} prod."));
            if (reason is null)
                return AppResult<JsonElement>.Fail(AppErrorFactory.Validation($"Give a reason to {request.OperationName} prod."));
        }

        foreach (var id in await fleets.GetRunningIdsAsync(cancellationToken))
        {
            var fleet = await fleets.GetAsync(id, cancellationToken);
            if (fleet?.Items.Any(item => item.Target == target.Slug && item.Status is FleetItemStatus.Waiting or FleetItemStatus.Running) == true)
                return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(
                    $"A fleet rollout is promoting {target.Slug}; wait for it or cancel it first."));
        }

        var result = await gateway.ControlAsync(target.Slug, request.OperationName, request.Service, request.Actor, reason, cancellationToken);
        if (result is not AppResult<JsonElement>.Failure { Error.Type: AppErrorType.Conflict })
            following.Wake();
        return result;
    }
}
