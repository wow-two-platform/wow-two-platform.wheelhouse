using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the query that picks deployments: repeat <c>workspace</c>, <c>product</c> and
/// <c>service</c> (<c>product/service</c>) to combine entities, <c>status</c> (<c>all</c>, <c>needs-action</c>,
/// <c>look</c>, <c>succeeded</c>, <c>not-applied</c>; the old <c>failed</c> keeps its meaning), <c>notApplied</c> to list
/// the deploys that changed nothing beside the others, and an ISO-8601 <c>from</c> (inclusive) and <c>to</c>
/// (exclusive).</summary>
public sealed record DeploymentFilterApiRequest
{
    /// <summary>Gets the workspace slugs whose products' deployments match.</summary>
    public string[]? Workspace { get; init; }

    /// <summary>Gets the product slugs whose deployments match.</summary>
    public string[]? Product { get; init; }

    /// <summary>Gets the services, as <c>product/service</c>, whose changes match.</summary>
    public string[]? Service { get; init; }

    /// <summary>Gets which states match, by wire name.</summary>
    public string? Status { get; init; }

    /// <summary>Gets whether the deploys that changed nothing show beside the others.</summary>
    public bool NotApplied { get; init; }

    /// <summary>Gets the earliest submission instant that matches.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the instant matching submissions precede.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Maps the query to the application's filter; a status no filter has maps outside the enum, which the
    /// filter's validator refuses.</summary>
    /// <returns>The filter.</returns>
    public DeploymentFilter ToFilter() => new()
    {
        Workspaces = Workspace ?? [],
        Products = Product ?? [],
        Services = Service ?? [],
        Status = DeploymentStatusFilters.Parse(Status) ?? (DeploymentStatusFilter)(-1),
        IncludeNotApplied = NotApplied,
        From = From,
        To = To,
    };
}
