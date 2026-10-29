using FluentValidation;
using Wheelhouse.Application.Deployments.History.Queries;
using WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;

namespace Wheelhouse.Application.Deployments.History.Validators;

/// <summary>Validates a history page request.</summary>
public sealed class DeploymentHistoryQueryValidator : AbstractValidator<DeploymentHistoryQuery>
{
    /// <summary>Configures the page rules.</summary>
    public DeploymentHistoryQueryValidator()
    {
        RuleFor(x => x.Filter).SetValidator(new DeploymentFilterValidator());
        RuleFor(x => x.Limit).InclusiveBetween(1, DeploymentHistoryQuery.MaxLimit).WithMessage("'limit' must be 1-100.");
        RuleFor(x => x.Cursor!)
            .Must(cursor => PageTokenMapper.TryDecode(cursor, DeploymentHistoryQuery.CursorKeys, out _)).When(x => x.Cursor is not null)
            .WithMessage("'cursor' must be the cursor a previous page answered with.");
    }
}
