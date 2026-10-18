namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents finished rollouts per day over a range, for a calendar.</summary>
/// <param name="From">The range's first local date.</param>
/// <param name="To">The range's last local date.</param>
/// <param name="TimeZone">The IANA time zone the days are counted in.</param>
/// <param name="Days">Only the days with a finished rollout, in date order.</param>
/// <param name="Products">The busiest products over the range, most finished rollouts first (then by slug), at most
/// <see cref="BusiestLimit"/>.</param>
public sealed record DeploymentActivityDto(
    DateOnly From, DateOnly To, string TimeZone, IReadOnlyList<DeploymentDayDto> Days,
    IReadOnlyList<DeploymentProductCountDto> Products)
{
    /// <summary>Holds how many of the busiest products the calendar names.</summary>
    public const int BusiestLimit = 5;
}
