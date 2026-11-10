using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates a link: target slugs, and two configuration paths the runner accepts, neither one the render
/// derives.</summary>
public sealed class SettingLinkSetCommandValidator : AbstractValidator<SettingLinkSetCommand>
{
    /// <summary>Configures the rules.</summary>
    public SettingLinkSetCommandValidator()
    {
        RuleFor(x => x.Target).MustBeNameSlug();
        RuleFor(x => x.SourceTarget).MustBeNameSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
        RuleFor(x => x.SourceKey).Must(DeploymentSettingsService.IsKey)
            .WithMessage("A source key is a configuration path such as Section:Key.");
        RuleFor(x => x.Key).Must(key => !DeploymentSettingsService.IsDerived(key)).When(x => DeploymentSettingsService.IsKey(x.Key))
            .WithMessage(x => x.Key + " is derived at each deploy; it links to nothing.");
        RuleFor(x => x.SourceKey).Must(key => !DeploymentSettingsService.IsDerived(key)).When(x => DeploymentSettingsService.IsKey(x.SourceKey))
            .WithMessage(x => x.SourceKey + " is derived at each deploy of its own product; link to a key it holds.");
    }
}
