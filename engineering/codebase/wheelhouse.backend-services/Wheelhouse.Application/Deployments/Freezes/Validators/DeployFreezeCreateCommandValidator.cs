using FluentValidation;
using Wheelhouse.Application.Deployments.Freezes.Commands;
using Wheelhouse.Application.Inventory.Extensions;

namespace Wheelhouse.Application.Deployments.Freezes.Validators;

/// <summary>Validates a freeze: an environment only with a product, and a one-line reason of 300 characters at most.</summary>
public sealed class DeployFreezeCreateCommandValidator : AbstractValidator<DeployFreezeCreateCommand>
{
    /// <summary>Configures the rules.</summary>
    public DeployFreezeCreateCommandValidator()
    {
        RuleFor(x => x.Product!).MustBeProductKey().When(x => x.Product is not null);
        RuleFor(x => x.Environment).Must(FreezeRules.IsEnvironment).WithMessage("'Environment' must be dev or prod.");
        RuleFor(x => x.Environment).Null().When(x => x.Product is null).WithMessage("Name the product an environment belongs to.");
        RuleFor(x => x.Reason).Must(FreezeRules.IsReason).WithMessage("'Reason' must be one line of 300 characters at most.");
    }
}
