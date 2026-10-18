using FluentValidation;
using Wheelhouse.Application.Deployments.History.Queries;

namespace Wheelhouse.Application.Deployments.History.Validators;

/// <summary>Validates the filter of a <see cref="DeploymentIssuesQuery"/>.</summary>
public sealed class DeploymentIssuesQueryValidator : AbstractValidator<DeploymentIssuesQuery>
{
    /// <summary>Configures the filter rules.</summary>
    public DeploymentIssuesQueryValidator() => RuleFor(x => x.Filter).SetValidator(new DeploymentFilterValidator());
}
