namespace Wheelhouse.Domain.Deployments.Enums;

/// <summary>Refers to why the control plane deploys a release on its own.</summary>
public enum DeploymentCause
{
    /// <summary>A new release of a product that auto-deploys to dev.</summary>
    AutoDev = 0,

    /// <summary>A release that passed its dev watch window, promoted to prod for a product that auto-promotes.</summary>
    AutoPromote = 1,

    /// <summary>An apply of edited settings and secrets the owner scheduled for a set time: the running release deploys
    /// again with the values of that moment.</summary>
    SettingsApply = 2,
}
