using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates a cancelled settings apply: a target slug.</summary>
public sealed class SettingsApplyCancelCommandValidator : AbstractValidator<SettingsApplyCancelCommand>
{
    /// <summary>Configures the rules.</summary>
    public SettingsApplyCancelCommandValidator() => RuleFor(x => x.Target).MustBeNameSlug();
}
