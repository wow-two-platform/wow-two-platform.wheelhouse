using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Application.Targets.Models;

/// <summary>Represents one environment of a product on one server, as the operator edits it.</summary>
public sealed record TargetDto
{
    /// <summary>Gets the name deployments and the runner know the target by.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the slug of the product the target runs.</summary>
    public required string Product { get; init; }

    /// <summary>Gets the slug of the server the target runs on.</summary>
    public required string Server { get; init; }

    /// <summary>Gets which stage of the product the target runs.</summary>
    public required DeploymentEnvironment Environment { get; init; }

    /// <summary>Gets the Docker network the target's services share with the host's platform services.</summary>
    public required string Network { get; init; }

    /// <summary>Gets the folder on the host the runner keeps the target's releases under.</summary>
    public required string Root { get; init; }

    /// <summary>Gets each service's settings file on the host.</summary>
    public required IReadOnlyList<TargetSettingDto> Settings { get; init; }

    /// <summary>Gets the requests that prove a rollout.</summary>
    public required IReadOnlyList<SmokeCheckDto> SmokeChecks { get; init; }

    /// <summary>Gets the hosts of sites the server's pattern does not cover.</summary>
    public required IReadOnlyList<SiteHostDto> Sites { get; init; }
}
