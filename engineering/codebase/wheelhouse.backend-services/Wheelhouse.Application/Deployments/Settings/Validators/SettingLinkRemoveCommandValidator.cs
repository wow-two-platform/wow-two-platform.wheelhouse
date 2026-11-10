using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates an unlink: a target slug and a configuration path.</summary>
public sealed class SettingLinkRemoveCommandValidator : AbstractValidator<SettingLinkRemoveCommand>
{
    /// <summary>Configures the rules.</summary>
    public SettingLinkRemoveCommandValidator()
    {
        RuleFor(x => x.Target).MustBeNameSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
    }
}
