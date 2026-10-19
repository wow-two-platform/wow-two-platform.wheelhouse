using FluentValidation;
using Wheelhouse.Application.Deployments.Settings.Commands;
using Wheelhouse.Application.Deployments.Settings.Services;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.Settings.Validators;

/// <summary>Validates a removed key: a product slug and a configuration path.</summary>
public sealed class ProductSettingKeyRemoveCommandValidator : AbstractValidator<ProductSettingKeyRemoveCommand>
{
    /// <summary>Configures the rules.</summary>
    public ProductSettingKeyRemoveCommandValidator()
    {
        RuleFor(x => x.Product).MustBeSlug();
        RuleFor(x => x.Key).Must(DeploymentSettingsService.IsKey).WithMessage("A key is a configuration path such as Section:Key.");
    }
}
