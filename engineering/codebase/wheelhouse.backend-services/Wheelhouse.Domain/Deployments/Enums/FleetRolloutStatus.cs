namespace Wheelhouse.Domain.Deployments.Enums;

/// <summary>Refers to where a fleet rollout stands.</summary>
public enum FleetRolloutStatus
{
    /// <summary>It promotes its products one at a time.</summary>
    Running = 0,

    /// <summary>Every product's rollout succeeded.</summary>
    Succeeded = 1,

    /// <summary>A product's rollout failed or was refused; the products after it were not started.</summary>
    Failed = 2,

    /// <summary>The owner cancelled it between products; the products after the last one were not started.</summary>
    Cancelled = 3,
}
