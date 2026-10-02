namespace Wheelhouse.Domain.Targets.Models;

/// <summary>Represents the host one of a target's sites answers on, where the server's pattern does not cover it.</summary>
public sealed record SiteHostValueObject
{
    /// <summary>Gets the site's name, such as <c>app</c>.</summary>
    public required string Site { get; init; }

    /// <summary>Gets the host name, such as <c>app.example.com</c>.</summary>
    public required string Host { get; init; }
}
