using FluentValidation;
using Wheelhouse.Application.Deployments.Freezes.Commands;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.Freezes.Validators;

/// <summary>Validates lifting a freeze: an environment only with a product.</summary>
public sealed class DeployFreezeDeleteCommandValidator : AbstractValidator<DeployFreezeDeleteCommand>
{
    /// <summary>Configures the rules.</summary>
    public DeployFreezeDeleteCommandValidator()
    {
        RuleFor(x => x.Product!).MustBeProductKey().When(x => x.Product is not null);
        RuleFor(x => x.Environment).Must(FreezeRules.IsEnvironment).WithMessage("'Environment' must be dev or prod.");
        RuleFor(x => x.Environment).Null().When(x => x.Product is null).WithMessage("Name the product an environment belongs to.");
    }
}
