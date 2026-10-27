using Wheelhouse.Application.Following;

namespace Wheelhouse.Application.Deployments.Following.Services;

/// <summary>When the deployment follower reads the runner: woken by every deploy, control and reconcile the console
/// submits, then while a submission is unfinished.</summary>
/// <param name="time">The clock.</param>
public sealed class DeploymentFollowSchedule(TimeProvider time) : FollowSchedule(time);
