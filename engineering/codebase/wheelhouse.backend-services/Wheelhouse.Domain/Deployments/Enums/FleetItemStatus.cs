namespace Wheelhouse.Domain.Deployments.Enums;

/// <summary>Refers to where one product of a fleet rollout stands.</summary>
public enum FleetItemStatus
{
    /// <summary>It waits for the products before it.</summary>
    Waiting = 0,

    /// <summary>Its rollout was submitted and has not finished.</summary>
    Running = 1,

    /// <summary>Its rollout succeeded.</summary>
    Succeeded = 2,

    /// <summary>Its rollout failed, rolled back or was interrupted, or gave no outcome in time.</summary>
    Failed = 3,

    /// <summary>A freeze, a gate or the runner refused its submission; nothing changed.</summary>
    Refused = 4,

    /// <summary>The fleet stopped before it: a product before it failed, or the owner cancelled.</summary>
    NotStarted = 5,
}
