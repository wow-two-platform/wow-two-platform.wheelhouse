namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents which deployments a view shows: the entities it covers, the outcomes and the time range.</summary>
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

    /// <summary>Gets which outcomes match.</summary>
    public DeploymentStatusFilter Status { get; init; } = DeploymentStatusFilter.All;

    /// <summary>Gets the earliest submission instant that matches, inclusive.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the instant submissions must precede, exclusive.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Gets whether the filter names any workspace, product or service.</summary>
    public bool HasEntities => Workspaces.Count > 0 || Products.Count > 0 || Services.Count > 0;
}
