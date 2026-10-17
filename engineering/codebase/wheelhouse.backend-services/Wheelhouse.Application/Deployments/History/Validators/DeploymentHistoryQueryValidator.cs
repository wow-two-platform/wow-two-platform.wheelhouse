using FluentValidation;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;

namespace Wheelhouse.Application.Deployments.History.Validators;

/// <summary>Validates a history page request.</summary>
public sealed class DeploymentHistoryQueryValidator : AbstractValidator<DeploymentHistoryQuery>
{
    /// <summary>Configures the page rules.</summary>
    public DeploymentHistoryQueryValidator()
    {
        RuleFor(x => x.Filter).SetValidator(new DeploymentFilterValidator());
        RuleFor(x => x.Limit).InclusiveBetween(1, 100).WithMessage("'limit' must be 1-100.");
        RuleFor(x => x.Cursor!)
            .Must(cursor => DeploymentCursor.TryRead(cursor, out _)).When(x => x.Cursor is not null)
            .WithMessage("'cursor' must be the cursor a previous page answered with.");
    }
}
