using FluentValidation;
using Wheelhouse.Application.Deployments.Fleets.Queries;

namespace Wheelhouse.Application.Deployments.Fleets.Validators;

/// <summary>Validates a fleet listing: 1-50 fleets.</summary>
public sealed class FleetRolloutsQueryValidator : AbstractValidator<FleetRolloutsQuery>
{
    /// <summary>Configures the rules.</summary>
    public FleetRolloutsQueryValidator() => RuleFor(x => x.Limit).InclusiveBetween(1, 50);
}
