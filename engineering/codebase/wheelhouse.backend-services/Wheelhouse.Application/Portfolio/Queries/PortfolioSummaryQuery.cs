using Wheelhouse.Application.Portfolio.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Portfolio.Queries;

/// <summary>Summarizes every product's deployments for the Products page: per environment the latest rollout, the one
/// serving and finished rollouts per week; per workspace finished rollouts per week.</summary>
/// <param name="Weeks">How many weeks, the current one last.</param>
/// <param name="TimeZone">The IANA time zone weeks start on Monday in.</param>
/// <param name="To">The instant the current week contains; now when absent.</param>
public sealed record PortfolioSummaryQuery(int Weeks, string TimeZone, DateTimeOffset? To = null)
    : IQuery<AppResult<PortfolioSummaryDto>>
{
    /// <summary>Holds the weeks a summary covers unless asked otherwise.</summary>
    public const int DefaultWeeks = 12;

    /// <summary>Holds the most weeks a summary covers.</summary>
    public const int MaxWeeks = 52;
}
