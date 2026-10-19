namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Holds what a deployment amounts to by what its environment runs because of it; the eight states are
/// mutually exclusive, and the wire carries these lowercase names.</summary>
public static class DeploymentState
{
    /// <summary>The environment is changing: scheduled, queued, waiting or rolling out.</summary>
    public const string InProgress = "in_progress";

    /// <summary>The environment runs the new release, healthy.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The environment runs the new release, not all of it healthy.</summary>
    public const string Degraded = "degraded";

    /// <summary>The environment runs the previous release again, whole.</summary>
    public const string RolledBack = "rolled_back";

    /// <summary>The environment runs a mix of releases.</summary>
    public const string Partial = "partial";

    /// <summary>The environment is broken or in an unknown state and needs action.</summary>
    public const string Failed = "failed";

    /// <summary>The environment is unchanged: the deploy was refused, replaced, cancelled, expired or stopped before it
    /// changed anything.</summary>
    public const string NotApplied = "not_applied";

    /// <summary>The control plane holds no readable receipt.</summary>
    public const string Unknown = "unknown";
}
