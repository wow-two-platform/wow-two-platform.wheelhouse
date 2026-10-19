using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Freezes.Services;
using Wheelhouse.Application.Deployments.Settings.Constants;
using Wheelhouse.Application.Deployments.Settings.Services;
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
/// validation — is kept as an intent, so the history lists it as a deploy that changed nothing. Before submitting, a
/// target's settings are planned: a required key no layer sets, on a service without a host file, refuses the deploy,
/// and the environment's stored settings and vault secrets go to the runner for the target to render. A submitted deploy
/// records what it rendered, by fingerprint, and which keys changed, so a failure can point at them.</summary>
public sealed class DeploymentStartCommandHandler(
    IDeploymentGateway gateway, ITargetsRepository targets, IProductsRepository products, DeployFreezeService freezes,
    IDeploymentAutomationStore intents, TimeProvider time, DeploymentSettingsService settings)
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

        Settings.Models.RenderValues? values = null;
        if (target is not null)
        {
            var prepared = await PrepareAsync(request, target, cancellationToken);
            if (prepared is AppResult<Settings.Models.RenderValues>.Failure refused)
                return AppResult<JsonElement>.Fail(refused.Error);
            values = ((AppResult<Settings.Models.RenderValues>.Success)prepared).Data;
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var result = await gateway.StartAsync(request.Target, request.Release, request.Actor, request.Confirm, request.SkipDevPass,
            reason, request.Fleet, values?.Values, cancellationToken);
        if (target is not null && values is not null && result is AppResult<JsonElement>.Success { Data: var submitted }
            && submitted.ValueKind == JsonValueKind.Object && submitted.TryGetProperty("id", out var jobId)
            && Guid.TryParse(jobId.GetString(), out var job))
            await settings.RecordAsync(target.Slug, job, request.Release, values.Keys, time.GetUtcNow(), cancellationToken);
        // A conflict is the runner refusing before it recorded anything; an unavailable runner proves nothing either way.
        if (target is not null && result is AppResult<JsonElement>.Failure { Error: { Type: AppErrorType.Conflict } refusal })
            await RecordAsync(request, target, RefusalOf(refusal.Message), refusal.Message, cancellationToken);
        return result;
    }

    /// <summary>Plans the target's settings for the release and reads the values the target renders: a required key no
    /// layer sets refuses the deploy, kept as an intent like any refusal. @internal</summary>
    private async Task<AppResult<Settings.Models.RenderValues>> PrepareAsync(
        DeploymentStartCommand request, TargetEntity target, CancellationToken ct)
    {
        var plan = await settings.PlanAsync(target.Slug, request.Release, ct);
        if (plan is AppResult<Settings.Models.DeploySettingsDto>.Failure failed)
        {
            if (failed.Error.Type == AppErrorType.Conflict)
                await RecordAsync(request, target, RefusalOf(failed.Error.Message), failed.Error.Message, ct);
            return AppResult<Settings.Models.RenderValues>.Fail(failed.Error);
        }
        var planned = ((AppResult<Settings.Models.DeploySettingsDto>.Success)plan).Data;
        var missing = planned.Services
            .Where(service => !service.HostFile)
            .Select(service => (service.Name, Keys: service.Keys.Where(key => key.Source == SettingSources.Missing).Select(key => key.Key).ToList()))
            .Where(service => service.Keys.Count > 0)
            .ToList();
        var extra = planned.Extra.Where(key => key.Source == SettingSources.Missing).Select(key => key.Key).ToList();
        if (extra.Count > 0)
            missing.Add(("every service", extra));
        if (missing.Count > 0)
        {
            var message = "Fill in the required settings first: "
                + string.Join("; ", missing.Select(service => service.Name + ": " + string.Join(", ", service.Keys))) + ".";
            await RecordAsync(request, target, DeploymentRefusal.Validation, message, ct);
            return AppResult<Settings.Models.RenderValues>.Fail(AppErrorFactory.Conflict(message));
        }
        var product = await products.GetByIdAsync(target.ProductId, ct);
        if (product is null)
            return AppResult<Settings.Models.RenderValues>.Fail(AppErrorFactory.NotFound($"Target '{target.Slug}' has no product."));
        var values = await settings.ValuesAsync(target, product, ct);
        // A vault that cannot render the environment's secrets holds the deploy, kept as a refusal like any other.
        if (values is AppResult<Settings.Models.RenderValues>.Failure { Error: var error })
            await RecordAsync(request, target, DeploymentRefusal.Validation, error.Message, ct);
        return values;
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
