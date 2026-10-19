using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Freezes.Services;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments;

/// <summary>Handles a deploy. A freeze covering the target refuses an automatic deploy outright and an owner's deploy
/// unless it overrides the freeze with a reason; the runner's own gates follow. A target the inventory lacks is left to
/// the runner, which refuses it. A refusal of an inventory target — the freeze, a promotion gate or the runner's
/// validation — is kept as an intent, so the history lists it as a deploy that changed nothing.</summary>
public sealed class DeploymentStartCommandHandler(
    IDeploymentGateway gateway, ITargetsRepository targets, IProductsRepository products, DeployFreezeService freezes,
    IDeploymentAutomationStore intents, TimeProvider time)
    : ICommandHandler<DeploymentStartCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(DeploymentStartCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        if (target is not null && await freezes.CoveringAsync(target.ProductId, target.Environment, cancellationToken) is { } freeze)
        {
            var slug = freeze.ProductId is { } id ? (await products.GetByIdAsync(id, cancellationToken))?.Slug : null;
            var covers = DeployFreezeService.Map(freeze, slug).Covers;
            if (request.Cause is not null || !request.OverrideFreeze)
            {
                await RecordAsync(request, target, DeploymentRefusal.Freeze, "Frozen: " + covers + ".", cancellationToken);
                return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(request.Cause is not null
                    ? $"Deploys to {request.Target} are frozen ({covers}); the automatic deploy was skipped."
                    : $"Deploys to {request.Target} are frozen ({covers}). Override the freeze with a reason to deploy anyway."));
            }
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var result = await gateway.StartAsync(request.Target, request.Release, request.Actor, request.Confirm, request.SkipDevPass,
            reason, request.Fleet, cancellationToken);
        // A conflict is the runner refusing before it recorded anything; an unavailable runner proves nothing either way.
        if (target is not null && result is AppResult<JsonElement>.Failure { Error: { Type: AppErrorType.Conflict } refusal })
            await RecordAsync(request, target, RefusalOf(refusal.Message), refusal.Message, cancellationToken);
        return result;
    }

    /// <summary>What refused a deploy, read from the runner's static rule text: a dev pass or check is the promotion
    /// gate, anything else the release's or the target's validation.</summary>
    /// <param name="message">The refusal's display-safe text.</param>
    /// <returns>The refusal.</returns>
    public static DeploymentRefusal RefusalOf(string? message) =>
        message is not null && (message.Contains("dev pass", StringComparison.OrdinalIgnoreCase)
            || message.Contains("to dev first", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dev check", StringComparison.OrdinalIgnoreCase))
            ? DeploymentRefusal.Gate
            : DeploymentRefusal.Validation;

    /// <summary>Keeps a refused deploy for the history. @internal</summary>
    private async Task RecordAsync(
        DeploymentStartCommand request, TargetEntity target, DeploymentRefusal refusal, string? message, CancellationToken ct) =>
        await intents.AddIntentAsync(new DeploymentIntentEntity
        {
            Id = Guid.NewGuid(),
            Target = target.Slug,
            Product = (await products.GetByIdAsync(target.ProductId, ct))?.Slug,
            Environment = target.Environment.ToString().ToLowerInvariant(),
            BundleId = request.Release,
            Release = request.ReleaseLabel ?? request.Release,
            Actor = request.Actor,
            Outcome = DeploymentIntentOutcome.Refused,
            Refusal = refusal,
            Message = message is { Length: > 300 } ? message[..300] : message,
            Cause = request.Cause,
            FleetId = request.Fleet,
            At = time.GetUtcNow(),
        }, ct);
}
