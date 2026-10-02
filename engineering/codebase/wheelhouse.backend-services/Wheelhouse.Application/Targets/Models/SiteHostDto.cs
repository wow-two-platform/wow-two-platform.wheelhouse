namespace Wheelhouse.Application.Targets.Models;

/// <summary>Represents the host one of a target's sites answers on.</summary>
public sealed record SiteHostDto
{
    /// <summary>Gets the site's name.</summary>
    public required string Site { get; init; }

    /// <summary>Gets the host name.</summary>
    public required string Host { get; init; }
}
