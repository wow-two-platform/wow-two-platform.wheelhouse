namespace Wheelhouse.Application.InfraJobs;

/// <summary>Represents one private site a placement put on the tailnet: the environment its node serves and the node's
/// tailnet host, such as <c>ocharo-motion-dev.example.ts.net</c>.</summary>
/// <param name="Site">The site's name, such as <c>motion</c>.</param>
/// <param name="Environment">The environment the node serves: <c>dev</c> or <c>prod</c>.</param>
/// <param name="Host">The node's tailnet host name.</param>
public sealed record InfraPlacedSite(string Site, string Environment, string Host);
