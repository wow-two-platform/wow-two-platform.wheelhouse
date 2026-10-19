namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents which deployments a view shows: the entities it covers, the states and the time range.</summary>
/// <remarks>Entities combine as a union: a deployment matches when it belongs to any listed workspace or product, or
/// changed any listed service. No entity at all means every deployment.</remarks>
public sealed record DeploymentFilter
{
    /// <summary>Gets the workspace slugs whose products' deployments match.</summary>
    public IReadOnlyList<string> Workspaces { get; init; } = [];

    /// <summary>Gets the product slugs whose deployments match.</summary>
    public IReadOnlyList<string> Products { get; init; } = [];

    /// <summary>Gets the services, as <c>product/service</c>, whose changes match.</summary>
    public IReadOnlyList<string> Services { get; init; } = [];

    /// <summary>Gets which states match.</summary>
    public DeploymentStatusFilter Status { get; init; } = DeploymentStatusFilter.All;

    /// <summary>Gets whether deployments that changed nothing show beside the others; the <c>not-applied</c> status shows
    /// them anyway.</summary>
    public bool IncludeNotApplied { get; init; }

    /// <summary>Gets whether only each target's latest applied deploy of a local day shows: earlier attempts that day are
    /// hidden, so a day whose last deploy succeeded reads as a success. The deploys that changed nothing follow
    /// <see cref="IncludeNotApplied"/> alone.</summary>
    public bool LatestOfDay { get; init; }

    /// <summary>Gets the IANA time zone <see cref="LatestOfDay"/> counts days in.</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>Gets the earliest submission instant that matches, inclusive.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the instant submissions must precede, exclusive.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Gets whether the filter names any workspace, product or service.</summary>
    public bool HasEntities => Workspaces.Count > 0 || Products.Count > 0 || Services.Count > 0;
}
