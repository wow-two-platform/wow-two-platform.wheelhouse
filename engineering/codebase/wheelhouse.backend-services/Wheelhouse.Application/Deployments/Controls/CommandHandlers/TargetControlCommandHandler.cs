using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Controls.Commands;
using Wheelhouse.Application.Deployments.Following.Services;
using Wheelhouse.Application.Targets.Services;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Controls.CommandHandlers;

/// <summary>Handles <see cref="TargetControlCommand"/>: Wheelhouse's own environments are refused (the console would
/// stop itself, or lose its own data on a reset or a copy; <c>self-deploy.py</c> and a promotion replace them). A copy's
/// source must be another environment on the same server, never Wheelhouse's own and never this product's same
/// environment, since the runner reads it on that server's platform PostgreSQL. Prod needs its
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
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(request.Operation switch
            {
                ControlOperation.Reset => $"{target.Slug} runs Wheelhouse itself; its database and migrations are never reset from the console.",
                ControlOperation.Run => $"{target.Slug} runs Wheelhouse itself; its services never run a one-off from the console.",
                ControlOperation.Copy => $"{target.Slug} runs Wheelhouse itself; its database is never replaced from the console.",
                _ => $"{target.Slug} runs Wheelhouse itself; its services are never stopped or restarted from the console.",
            }));

        if (request.Operation == ControlOperation.Copy
            && await RefuseSourceAsync(target, request.From!, cancellationToken) is { } refusal)
            return AppResult<JsonElement>.Fail(refusal);

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

        var result = await gateway.ControlAsync(
            target.Slug, request.OperationName, request.Service, request.Set, request.Actor, reason, request.Command,
            request.Input, request.Operation == ControlOperation.Copy ? request.From : null, cancellationToken);
        if (result is not AppResult<JsonElement>.Failure { Error.Type: AppErrorType.Conflict })
            following.Wake();
        return result;
    }

    /// <summary>Why a copy may not read <paramref name="from"/> into <paramref name="target"/>: it is unknown, on another
    /// server, Wheelhouse's own, or the same product's same environment; null when it may. @internal</summary>
    private async Task<AppError?> RefuseSourceAsync(TargetEntity target, string from, CancellationToken cancellationToken)
    {
        var source = await targets.GetBySlugAsync(from, cancellationToken);
        if (source is null)
            return AppErrorFactory.Validation($"There is no environment {from} to copy from.");
        if (source.ServerId != target.ServerId)
            return AppErrorFactory.Conflict(
                $"A copy reads the source on the same platform PostgreSQL; {from} is on another server.");
        var product = await products.GetByIdAsync(source.ProductId, cancellationToken);
        if (product is not null && SelfTargets.IsSelf(product.Slug))
            return AppErrorFactory.Conflict($"{from} runs Wheelhouse itself; its database is never copied from the console.");
        if (source.ProductId == target.ProductId && source.Environment == target.Environment)
            return AppErrorFactory.Conflict($"{from} is {target.Slug}'s own product and environment; copy from another one.");
        return null;
    }
}
