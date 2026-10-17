using FluentValidation;
using Wheelhouse.Application.Deployments.History.Queries;

namespace Wheelhouse.Application.Deployments.History.Validators;

/// <summary>Validates the filter of a <see cref="DeploymentRunningQuery"/>.</summary>
public sealed class DeploymentRunningQueryValidator : AbstractValidator<DeploymentRunningQuery>
{
    /// <summary>Configures the filter rules.</summary>
    public DeploymentRunningQueryValidator() => RuleFor(x => x.Filter).SetValidator(new DeploymentFilterValidator());
}
