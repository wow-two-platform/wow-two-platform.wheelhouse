using FluentValidation;
using Wheelhouse.Application.Deployments.History.Queries;

namespace Wheelhouse.Application.Deployments.History.Validators;

/// <summary>Validates the filter of a <see cref="DeploymentPromotionsQuery"/>.</summary>
public sealed class DeploymentPromotionsQueryValidator : AbstractValidator<DeploymentPromotionsQuery>
{
    /// <summary>Configures the filter rules.</summary>
    public DeploymentPromotionsQueryValidator() => RuleFor(x => x.Filter).SetValidator(new DeploymentFilterValidator());
}
