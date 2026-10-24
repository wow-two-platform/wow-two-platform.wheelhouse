using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Application.Targets.Models;

/// <summary>Represents one of Wheelhouse's own environments as it derives it: the consoles' network and folder on the
/// control host, the console's settings file the kit lays out, and the console site on this console's hostname.</summary>
public sealed record SelfTargetEnvironmentDto
{
    /// <summary>Gets the environment.</summary>
    public required DeploymentEnvironment Environment { get; init; }

    /// <summary>Gets the Docker network the consoles share with PostgreSQL and Traefik.</summary>
    public required string Network { get; init; }

    /// <summary>Gets the folder the runner keeps the console's releases under.</summary>
    public required string Root { get; init; }

    /// <summary>Gets the console's settings file on the control host.</summary>
    public required IReadOnlyList<TargetSettingDto> Settings { get; init; }

    /// <summary>Gets the console site's host; empty while this console names no hostname.</summary>
    public required IReadOnlyList<SiteHostDto> Sites { get; init; }
}
