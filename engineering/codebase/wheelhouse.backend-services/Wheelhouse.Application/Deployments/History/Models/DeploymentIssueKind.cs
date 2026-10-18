namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Holds what went wrong on a target, as the issues list names it; the wire carries these lowercase names.</summary>
public static class DeploymentIssueKind
{
    /// <summary>The latest rollout failed.</summary>
    public const string Failed = "failed";

    /// <summary>The latest rollout failed and the target restored the previous release.</summary>
    public const string RolledBack = "rolled_back";

    /// <summary>The latest rollout failed, and so did restoring the previous release.</summary>
    public const string RollbackFailed = "rollback_failed";

    /// <summary>The latest rollout stopped mid-way and was reconciled; nothing has deployed since.</summary>
    public const string Interrupted = "interrupted";

    /// <summary>A rollout has been live for longer than any rollout should take.</summary>
    public const string Stuck = "stuck";
}
