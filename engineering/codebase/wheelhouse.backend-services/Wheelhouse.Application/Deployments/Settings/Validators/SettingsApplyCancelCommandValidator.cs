using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates a cancelled settings apply: a target slug.</summary>
public sealed class SettingsApplyCancelCommandValidator : AbstractValidator<SettingsApplyCancelCommand>
{
    /// <summary>Configures the rules.</summary>
    public SettingsApplyCancelCommandValidator() => RuleFor(x => x.Target).MustBeSlug();
}
