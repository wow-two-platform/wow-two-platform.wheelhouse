namespace Wheelhouse.Application.Deployments.Automation.Constants;

/// <summary>Holds the names automatic deploys go by.</summary>
public static class AutomationConstants
{
    /// <summary>Holds the actor every automatic deploy records, in the job and the audit trail.</summary>
    public const string Actor = "automation";

    /// <summary>Holds the products their own CI deploys (<c>deploy: ci</c> in the wow2 catalog): the control plane never
    /// deploys them on its own. Wheelhouse is one; it never deploys itself.</summary>
    public static readonly IReadOnlySet<string> CiDeployedProducts = new HashSet<string>(StringComparer.Ordinal) { "wheelhouse" };
}
