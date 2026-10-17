namespace Wheelhouse.Domain.Targets.Enums;

/// <summary>Refers to which stage of a product a target runs: every <c>main</c> release deploys to dev, then is
/// promoted to prod.</summary>
/// <remarks>
/// <c>test</c> (value 1) was retired on 2026-10-17, when products moved to <c>main</c> only; migration
/// <c>013-drop-test-environment</c> removed its targets. Its value stays unused, so a client still sending 1 is
/// refused instead of reaching prod.
/// </remarks>
public enum DeploymentEnvironment
{
    /// <summary>Takes a build of any commit or branch.</summary>
    Dev = 0,

    /// <summary>Takes only a published release that succeeded on dev.</summary>
    Prod = 2,
}
