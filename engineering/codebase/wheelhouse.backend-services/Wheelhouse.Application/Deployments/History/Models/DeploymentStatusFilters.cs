namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Reads the status filter's wire names, the old ones included.</summary>
public static class DeploymentStatusFilters
{
    /// <summary>Reads a status filter's wire name: <c>all</c>, <c>needs-action</c>, <c>look</c>, <c>succeeded</c>,
    /// <c>not-applied</c>, or the old <c>failed</c> and <c>refused</c> (which reads as <c>not-applied</c>); case, dashes and
    /// underscores are ignored.</summary>
    /// <param name="value">The wire name; null or empty reads as <c>all</c>.</param>
    /// <returns>The filter, or null for a name no filter has.</returns>
    public static DeploymentStatusFilter? Parse(string? value) =>
        (value ?? "").Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "" or "all" => DeploymentStatusFilter.All,
            "succeeded" => DeploymentStatusFilter.Succeeded,
            "failed" => DeploymentStatusFilter.Failed,
            "needsaction" => DeploymentStatusFilter.NeedsAction,
            "look" => DeploymentStatusFilter.Look,
            "notapplied" or "refused" => DeploymentStatusFilter.NotApplied,
            _ => null,
        };
}
