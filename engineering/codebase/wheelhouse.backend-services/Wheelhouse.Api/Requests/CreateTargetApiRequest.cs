using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Domain.Targets.Models;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to add an environment of a product on a server.</summary>
public sealed record CreateTargetApiRequest
{
    /// <summary>Gets the name deployments and the runner will know the target by.</summary>
    public required string Slug { get; init; }    /// <summary>Gets the slug of the product the target runs.</summary>
    public required string Product { get; init; }

    /// <summary>Gets the slug of the server the target runs on.</summary>
    public required string Server { get; init; }

    /// <summary>Gets which stage of the product the target runs.</summary>
    public required DeploymentEnvironment Environment { get; init; }

    /// <summary>Gets the Docker network the target's services share with the host's platform services.</summary>
    public required string Network { get; init; }

    /// <summary>Gets the folder on the host the runner keeps the target's releases under.</summary>
    public string Root { get; init; } = "/srv/wheelhouse";

    /// <summary>Gets each service's settings file on the host.</summary>
    public IReadOnlyList<TargetSettingValueObject> Settings { get; init; } = [];

    /// <summary>Gets the requests that prove a rollout.</summary>
    public IReadOnlyList<SmokeCheckValueObject> SmokeChecks { get; init; } = [];

    /// <summary>Gets the hosts of sites the server's pattern does not cover.</summary>
    public IReadOnlyList<SiteHostValueObject> Sites { get; init; } = [];
}
