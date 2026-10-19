namespace Wheelhouse.Domain.Deployments.Enums;

/// <summary>Refers to how a deploy the control plane meant to run ended without changing its target.</summary>
public enum DeploymentIntentOutcome
{
    /// <summary>A freeze, a gate or the runner's validation refused it before anything was submitted.</summary>
    Refused = 0,

    /// <summary>A newer release took its place in the target's queue before it ran (latest wins).</summary>
    Superseded = 1,

    /// <summary>It was dropped before it ran: the owner cancelled it, or turned its automation off.</summary>
    Cancelled = 2,

    /// <summary>Its schedule passed without a chance to run.</summary>
    Expired = 3,

    /// <summary>The target stayed busy past its wait, so it gave up.</summary>
    Aborted = 4,
}
