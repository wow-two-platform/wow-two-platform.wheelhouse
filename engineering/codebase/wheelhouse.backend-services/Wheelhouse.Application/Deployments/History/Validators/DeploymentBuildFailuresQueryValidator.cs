using FluentValidation;
using Wheelhouse.Application.Deployments.History.Queries;

namespace Wheelhouse.Application.Deployments.History.Validators;

/// <summary>Validates the filter of a <see cref="DeploymentBuildFailuresQuery"/>.</summary>
public sealed class DeploymentBuildFailuresQueryValidator : AbstractValidator<DeploymentBuildFailuresQuery>
{
    /// <summary>Configures the filter rules.</summary>
    public DeploymentBuildFailuresQueryValidator() => RuleFor(x => x.Filter).SetValidator(new DeploymentFilterValidator());
}
