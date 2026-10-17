using FluentValidation;
using Wheelhouse.Application.Deployments.History.Queries;

namespace Wheelhouse.Application.Deployments.History.Validators;

/// <summary>Validates the filter of a <see cref="DeploymentStatsQuery"/>.</summary>
public sealed class DeploymentStatsQueryValidator : AbstractValidator<DeploymentStatsQuery>
{
    /// <summary>Configures the filter rules.</summary>
    public DeploymentStatsQueryValidator() => RuleFor(x => x.Filter).SetValidator(new DeploymentFilterValidator());
}
