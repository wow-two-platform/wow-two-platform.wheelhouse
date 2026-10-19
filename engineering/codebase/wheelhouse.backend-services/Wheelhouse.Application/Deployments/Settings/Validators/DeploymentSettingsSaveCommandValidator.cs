using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates setting values: configuration paths the runner accepts, each once, none the render derives; a
/// setting is one line of up to 2,000 characters, a secret up to 64 KiB.</summary>
public sealed class DeploymentSettingsSaveCommandValidator : AbstractValidator<DeploymentSettingsSaveCommand>
{
    /// <summary>Configures the rules.</summary>
    public DeploymentSettingsSaveCommandValidator()
    {
        RuleFor(x => x.Target).MustBeSlug();
        RuleFor(x => x.Values).NotEmpty().Must(values => values.Count <= 100)
            .WithMessage("Give between 1 and 100 values at once.");
        RuleFor(x => x.Values).Must(values => values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count() == values.Count)
            .WithMessage("Give each key once.");
        RuleForEach(x => x.Values).ChildRules(value =>
        {
            value.RuleFor(item => item.Key).Must(DeploymentSettingsService.IsKey)
                .WithMessage("A key is a configuration path such as Section:Key.");
            value.RuleFor(item => item.Key).Must(key => !DeploymentSettingsService.IsDerived(key))
                .WithMessage(item => item.Key + " is derived at each deploy; it takes no value.");
            value.RuleFor(item => item.Value).NotEmpty().WithMessage(item => item.Key + " needs a value.");
            value.RuleFor(item => item.Value).Must(text => text.Length <= 2000 && !text.Any(char.IsControl)).When(item => !item.Secret)
                .WithMessage(item => item.Key + " must be one line of 2,000 characters at most.");
            value.RuleFor(item => item.Value).Must(text => text.Length <= 65536).When(item => item.Secret)
                .WithMessage(item => item.Key + " must be 64 KiB at most.");
        });
    }
}
