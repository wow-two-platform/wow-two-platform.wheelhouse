using Wheelhouse.Application.Following;

namespace Wheelhouse.Application.InfraJobs;

/// <summary>When the infrastructure follower reads the runner: woken by every enrollment, placement and upkeep the
/// console starts, then while a job is unfinished or a finished one is left unrecorded.</summary>
/// <param name="time">The clock.</param>
public sealed class InfraFollowSchedule(TimeProvider time) : FollowSchedule(time);
