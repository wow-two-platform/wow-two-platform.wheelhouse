namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Refers to which outcomes a deployment view shows.</summary>
public enum DeploymentStatusFilter
{
    /// <summary>Every submission, finished or not.</summary>
    All,

    /// <summary>Rollouts that succeeded, with or without warnings.</summary>
    Succeeded,

    /// <summary>Rollouts that failed, rolled back, failed to roll back or were interrupted.</summary>
    Failed,
}
