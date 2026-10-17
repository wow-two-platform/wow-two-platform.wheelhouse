using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the query that picks deployments: repeat <c>workspace</c>, <c>product</c> and
/// <c>service</c> (<c>product/service</c>) to combine entities, <c>status</c> (<c>all</c>, <c>succeeded</c>,
/// <c>failed</c>), and an ISO-8601 <c>from</c> (inclusive) and <c>to</c> (exclusive).</summary>
public sealed record DeploymentFilterApiRequest
{
    /// <summary>Gets the workspace slugs whose products' deployments match.</summary>
    public string[]? Workspace { get; init; }

    /// <summary>Gets the product slugs whose deployments match.</summary>
    public string[]? Product { get; init; }

    /// <summary>Gets the services, as <c>product/service</c>, whose changes match.</summary>
    public string[]? Service { get; init; }

    /// <summary>Gets which outcomes match.</summary>
    public DeploymentStatusFilter Status { get; init; } = DeploymentStatusFilter.All;

    /// <summary>Gets the earliest submission instant that matches.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the instant matching submissions precede.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Maps the query to the application's filter.</summary>
    /// <returns>The filter.</returns>
    public DeploymentFilter ToFilter() => new()
    {
        Workspaces = Workspace ?? [],
        Products = Product ?? [],
        Services = Service ?? [],
        Status = Status,
        From = From,
        To = To,
    };
}
