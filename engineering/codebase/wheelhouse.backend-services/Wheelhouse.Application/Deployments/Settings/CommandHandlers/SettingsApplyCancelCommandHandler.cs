using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.CommandHandlers;

/// <summary>Handles <see cref="SettingsApplyCancelCommand"/>; a target without a scheduled apply is not found.</summary>
public sealed class SettingsApplyCancelCommandHandler(ITargetsRepository targets, ISettingsApplies applies)
    : ICommandHandler<SettingsApplyCancelCommand, AppResult<bool>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<bool>> HandleAsync(SettingsApplyCancelCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Target, cancellationToken);
        return target is not null && await applies.RemoveAsync(target.Id, cancellationToken)
            ? AppResult<bool>.Ok(true)
            : AppResult<bool>.Fail(AppErrorFactory.NotFound($"{request.Target} has no scheduled settings apply."));
    }
}
