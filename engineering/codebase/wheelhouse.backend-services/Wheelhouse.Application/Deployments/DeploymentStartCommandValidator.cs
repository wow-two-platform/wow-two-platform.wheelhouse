using FluentValidation;
using Wheelhouse.Application.Deployments.Freezes.Validators;

namespace Wheelhouse.Application.Deployments;

/// <summary>Validates a deploy's reason: one line of 300 characters at most, and given whenever the deploy overrides a
/// freeze or skips the dev pass.</summary>
public sealed class DeploymentStartCommandValidator : AbstractValidator<DeploymentStartCommand>
{
    /// <summary>Configures the rules.</summary>
    public DeploymentStartCommandValidator()
    {
        RuleFor(x => x.Reason).Must(FreezeRules.IsReason).WithMessage("'Reason' must be one line of 300 characters at most.");
        RuleFor(x => x.Reason).Must(reason => !string.IsNullOrWhiteSpace(reason)).When(x => x.OverrideFreeze)
            .WithMessage("Give a reason to override the freeze.");
        RuleFor(x => x.Reason).Must(reason => !string.IsNullOrWhiteSpace(reason)).When(x => x.SkipDevPass)
            .WithMessage("Give a reason to deploy without a dev pass.");
    }
}
