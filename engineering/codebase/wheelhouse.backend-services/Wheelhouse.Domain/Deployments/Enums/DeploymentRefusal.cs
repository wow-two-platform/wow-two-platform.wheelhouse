namespace Wheelhouse.Domain.Deployments.Enums;

/// <summary>Refers to what refused a deploy before it was submitted.</summary>
public enum DeploymentRefusal
{
    /// <summary>A promotion gate: prod takes only a release that passed dev.</summary>
    Gate = 0,

    /// <summary>A freeze covers the target.</summary>
    Freeze = 1,

    /// <summary>The runner found the release or the target invalid.</summary>
    Validation = 2,
}
