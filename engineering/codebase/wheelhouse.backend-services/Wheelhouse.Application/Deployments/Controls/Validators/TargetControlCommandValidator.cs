using FluentValidation;
using Wheelhouse.Application.Deployments.Controls.Commands;
using Wheelhouse.Application.Deployments.Freezes.Validators;
using Wheelhouse.Application.Inventory.Extensions;
using Wheelhouse.Domain.Deployments.Enums;

namespace Wheelhouse.Application.Deployments.Controls.Validators;

/// <summary>Validates a control: a target, a known operation, a service key when one is named — a restart always names
/// one, a reset never — and a one-line reason of 300 characters at most.</summary>
public sealed class TargetControlCommandValidator : AbstractValidator<TargetControlCommand>
{
    /// <summary>Configures the rules.</summary>
    public TargetControlCommandValidator()
    {
        RuleFor(x => x.Target).MustBeSlug();
        RuleFor(x => x.Operation).IsInEnum();
        RuleFor(x => x.Service!).MustBeSlug().When(x => x.Service is not null);
        RuleFor(x => x.Service).NotNull().When(x => x.Operation == ControlOperation.Restart)
            .WithMessage("Restart one service at a time.");
        RuleFor(x => x.Service).Null().When(x => x.Operation == ControlOperation.Reset)
            .WithMessage("A reset takes the environment's database, never one service.");
        RuleFor(x => x.Reason).Must(FreezeRules.IsReason).WithMessage("'Reason' must be one line of 300 characters at most.");
    }
}
