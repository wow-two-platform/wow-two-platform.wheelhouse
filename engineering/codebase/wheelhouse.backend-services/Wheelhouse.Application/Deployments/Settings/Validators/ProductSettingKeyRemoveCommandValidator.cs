using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates a removed key: a product slug and a configuration path.</summary>
public sealed class ProductSettingKeyRemoveCommandValidator : AbstractValidator<ProductSettingKeyRemoveCommand>
{
    /// <summary>Configures the rules.</summary>
    public ProductSettingKeyRemoveCommandValidator()
    {
        RuleFor(x => x.Product).MustBeNameSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
    }
}
