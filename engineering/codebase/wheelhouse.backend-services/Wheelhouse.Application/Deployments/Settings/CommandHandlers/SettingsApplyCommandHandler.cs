using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Models;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Domain.Deployments.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.CommandHandlers;

/// <summary>Handles <see cref="SettingsApplyCommand"/>: a time within a minute applies now; a later one is scheduled for
/// the target, replacing the one it had. A target that runs no release has nothing to apply to.</summary>
public sealed class SettingsApplyCommandHandler(
    ITargetsRepository targets, ISettingsApplies applies, SettingsApplyService service, DeploymentSettingsService settings,
    TimeProvider time)
    : ICommandHandler<SettingsApplyCommand, AppResult<SettingsApplyDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<SettingsApplyDto>> HandleAsync(SettingsApplyCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        if (target is null)
            return AppResult<SettingsApplyDto>.Fail(AppErrorFactory.NotFound($"Target '{request.Target}' was not found."));
        if (request.At is not { } at || at <= time.GetUtcNow().AddMinutes(1))
            return await service.ApplyNowAsync(target, request.Actor, request.Reason, request.Confirm, null, cancellationToken);
        if (await settings.RunningAsync(target.Slug, cancellationToken) is not { } running)
            return AppResult<SettingsApplyDto>.Fail(AppErrorFactory.Conflict(
                $"{target.Slug} runs no release yet; its settings apply with its first deploy."));
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        await applies.SetAsync(new SettingsApplyEntity { TargetId = target.Id, At = at, RequestedBy = request.Actor, Reason = reason },
            cancellationToken);
        return AppResult<SettingsApplyDto>.Ok(new SettingsApplyDto(target.Slug, "scheduled", at, request.Actor, null, running.Release));
    }
}
