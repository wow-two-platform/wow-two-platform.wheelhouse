namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Refers to which deployments a view shows by their state and the attention they need. The wire names are
/// <c>all</c>, <c>needs-action</c>, <c>look</c>, <c>succeeded</c> and <c>not-applied</c>; <c>failed</c> keeps its old
/// meaning one release for integrations.</summary>
public enum DeploymentStatusFilter
{
    /// <summary>Every deployment, finished or not; those that changed nothing only when the filter asks for them.</summary>
    All,

    /// <summary>Deployments that succeeded and need nothing.</summary>
    Succeeded,

    /// <summary>The old outcome filter: rollouts that failed, rolled back, failed to roll back or were interrupted.</summary>
    Failed,

    /// <summary>Deployments that need the owner's action: partial, failed.</summary>
    NeedsAction,

    /// <summary>Deployments worth a look: degraded, rolled back, a failed check or an expired schedule.</summary>
    Look,

    /// <summary>Deployments that changed nothing: refused, replaced, cancelled, expired or stopped before changes.</summary>
    NotApplied,
}
