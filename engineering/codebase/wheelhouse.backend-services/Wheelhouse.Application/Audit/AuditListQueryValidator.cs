using FluentValidation;
using WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;

namespace Wheelhouse.Application.Audit;

/// <summary>Validates an audit page request.</summary>
public sealed class AuditListQueryValidator : AbstractValidator<AuditListQuery>
{
    /// <summary>Configures the page rules.</summary>
    public AuditListQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, AuditListQuery.MaxPageSize).WithMessage("'pageSize' must be 1-200.");
        RuleFor(x => x.PageToken!)
            .Must(token => PageTokenMapper.TryDecode(token, AuditListQuery.PageTokenKeys, out _)).When(x => x.PageToken is not null)
            .WithMessage("'pageToken' must be the nextPageToken a previous page answered with.");
    }
}
