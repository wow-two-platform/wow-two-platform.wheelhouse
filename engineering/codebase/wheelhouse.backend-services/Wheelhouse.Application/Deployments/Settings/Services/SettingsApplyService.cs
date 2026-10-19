using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>Applies an environment's edited settings and secrets: its running release deploys again through the ordinary
/// deploy command, so the render takes the values of that moment and the runner recreates only the services whose
/// settings changed (<c>--force-recreate --no-deps</c>). Now, or at a set time from the scheduled pass.</summary>
public sealed partial class SettingsApplyService(
    ITargetsRepository targets,
    ISettingsApplies applies,
    DeploymentSettingsService settings,
    ISender sender,
    BackgroundActor background,
    TimeProvider time,
    ILogger<SettingsApplyService> logger)
{
    /// <summary>The reason every apply's deploy carries, so the history reads it as a settings deployment.</summary>
    public const string ReasonPrefix = "Settings apply";

    /// <summary>Submits the target's running release again with the values of now.</summary>
    /// <param name="target">The target.</param>
    /// <param name="actor">Who applies.</param>
    /// <param name="reason">The owner's one-line why.</param>
    /// <param name="confirm">The typed target ID, where the target asks for one.</param>
    /// <param name="cause">Why the control plane applies it on its own: <see cref="DeploymentCause.SettingsApply"/> for a
    /// scheduled apply, null for the owner's.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The submitted deploy, or why it was refused.</returns>
    public async Task<AppResult<SettingsApplyDto>> ApplyNowAsync(
        TargetEntity target, string actor, string? reason, string? confirm, DeploymentCause? cause, CancellationToken ct)
    {
        if (await settings.RunningAsync(target.Slug, ct) is not { } running)
            return AppResult<SettingsApplyDto>.Fail(AppErrorFactory.Conflict(
                $"{target.Slug} runs no release yet; its settings apply with its first deploy."));
        var why = ReasonPrefix + (string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim());
        var result = await sender.SendAsync(new DeploymentStartCommand(target.Slug, running.Bundle, actor, confirm,
            Cause: cause, Reason: why.Length > 200 ? why[..200] : why, ReleaseLabel: running.Release), ct);
        if (result is AppResult<JsonElement>.Failure failure)
            return AppResult<SettingsApplyDto>.Fail(failure.Error);
        var submitted = ((AppResult<JsonElement>.Success)result).Data;
        await applies.RemoveAsync(target.Id, ct);
        return AppResult<SettingsApplyDto>.Ok(new SettingsApplyDto(target.Slug, "now", null, actor,
            submitted.ValueKind == JsonValueKind.Object && submitted.TryGetProperty("id", out var id) ? id.GetString() : null,
            running.Release));
    }

    /// <summary>Runs every scheduled apply that is due, each for the owner who scheduled it. A due apply is dropped before
    /// it runs, so a refusal (a freeze, a target that runs nothing) is kept as a refused deploy and never retried.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>How many it submitted.</returns>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var submitted = 0;
        foreach (var apply in await applies.GetDueAsync(time.GetUtcNow(), ct))
        {
            await applies.RemoveAsync(apply.TargetId, ct);
            if (await targets.GetByIdAsync(apply.TargetId, ct) is not { } target)
                continue;
            background.Actor = apply.RequestedBy;
            var result = await ApplyNowAsync(target, apply.RequestedBy, apply.Reason, null, DeploymentCause.SettingsApply, ct);
            if (result is AppResult<SettingsApplyDto>.Success)
            {
                submitted++;
                LogApplied(logger, target.Slug);
            }
            else
                LogRefused(logger, target.Slug);
        }
        return submitted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied the scheduled settings of {Target}")]
    private static partial void LogApplied(ILogger logger, string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The scheduled settings apply of {Target} was refused")]
    private static partial void LogRefused(ILogger logger, string target);
}
