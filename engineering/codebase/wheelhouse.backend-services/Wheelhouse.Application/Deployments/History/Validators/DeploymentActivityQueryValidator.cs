using FluentValidation;
using Wheelhouse.Application.Deployments.History.Queries;

namespace Wheelhouse.Application.Deployments.History.Validators;

/// <summary>Validates an activity request: a known IANA time zone and a range of two years at most.</summary>
public sealed class DeploymentActivityQueryValidator : AbstractValidator<DeploymentActivityQuery>
{
    /// <summary>Holds the longest range a calendar covers.</summary>
    public const int MaxDays = 731;

    /// <summary>Configures the activity rules.</summary>
    public DeploymentActivityQueryValidator()
    {
        RuleFor(x => x.Filter).SetValidator(new DeploymentFilterValidator());
        RuleFor(x => x.TimeZone)
            .Must(zone => zone is { Length: > 0 and <= 64 } && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
            .WithMessage("'tz' must be an IANA time zone, such as Asia/Tashkent.");
        RuleFor(x => x.Filter)
            .Must(filter => filter.From is not { } from || filter.To is not { } to || (to - from).TotalDays <= MaxDays)
            .WithMessage($"The range may span {MaxDays} days at most.");
    }
}
