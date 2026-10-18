using FluentValidation;
using Wheelhouse.Application.Portfolio.Queries;

namespace Wheelhouse.Application.Portfolio.Validators;

/// <summary>Validates a portfolio summary request: 1 to 52 weeks in a known IANA time zone.</summary>
public sealed class PortfolioSummaryQueryValidator : AbstractValidator<PortfolioSummaryQuery>
{
    /// <summary>Configures the summary rules.</summary>
    public PortfolioSummaryQueryValidator()
    {
        RuleFor(x => x.Weeks)
            .InclusiveBetween(1, PortfolioSummaryQuery.MaxWeeks)
            .WithMessage($"'weeks' must be between 1 and {PortfolioSummaryQuery.MaxWeeks}.");
        RuleFor(x => x.TimeZone)
            .Must(zone => zone is { Length: > 0 and <= 64 } && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
            .WithMessage("'tz' must be an IANA time zone, such as Asia/Tashkent.");
    }
}
