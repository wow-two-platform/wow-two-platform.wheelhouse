using FluentValidation;
using Wheelhouse.Application.Deployments.Automation.Commands;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.Automation.Validators;

/// <summary>Validates a change to a product's automation choices.</summary>
public sealed class ProductAutomationUpdateCommandValidator : AbstractValidator<ProductAutomationUpdateCommand>
{
    /// <summary>Configures the rules: a product, and at least one choice.</summary>
    public ProductAutomationUpdateCommandValidator()
    {
        RuleFor(x => x.Product).MustBeProductKey();
        RuleFor(x => x)
            .Must(x => x.AutoDeployDev is not null || x.AutoPromote is not null)
            .WithName("automation")
            .WithMessage("Name autoDeployDev, autoPromote or both.");
    }
}
