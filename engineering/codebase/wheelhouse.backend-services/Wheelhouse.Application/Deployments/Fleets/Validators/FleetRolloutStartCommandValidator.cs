using FluentValidation;
using Wheelhouse.Application.Deployments.Fleets.Commands;
using Wheelhouse.Application.Deployments.Fleets.Services;
using Wheelhouse.Application.Deployments.Freezes.Validators;

namespace Wheelhouse.Application.Deployments.Fleets.Validators;

/// <summary>Validates a fleet: 1-20 products, each prod target once, slugs only, and a one-line reason of 300 characters
/// at most, given whenever it overrides a freeze.</summary>
public sealed class FleetRolloutStartCommandValidator : AbstractValidator<FleetRolloutStartCommand>
{
    private const string Slug = "^[a-z][a-z0-9-]{0,47}$";

    /// <summary>Configures the rules.</summary>
    public FleetRolloutStartCommandValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Choose at least one product to promote.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(FleetRolloutService.MaxItems)
            .WithMessage($"A fleet promotes {FleetRolloutService.MaxItems} products at most.");
        RuleFor(x => x.Items).Must(items => items.Select(item => item.Target).Distinct(StringComparer.Ordinal).Count() == items.Count)
            .WithMessage("A fleet promotes each prod target once.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.Target).Matches(Slug);
            item.RuleFor(x => x.Release).Matches(Slug);
            item.RuleFor(x => x.Confirm!).Matches(Slug).When(x => x.Confirm is not null);
        });
        RuleFor(x => x.Reason).Must(FreezeRules.IsReason).WithMessage("'Reason' must be one line of 300 characters at most.");
        RuleFor(x => x.Reason).Must(reason => !string.IsNullOrWhiteSpace(reason)).When(x => x.OverrideFreeze)
            .WithMessage("Give a reason to override the freeze.");
    }
}
