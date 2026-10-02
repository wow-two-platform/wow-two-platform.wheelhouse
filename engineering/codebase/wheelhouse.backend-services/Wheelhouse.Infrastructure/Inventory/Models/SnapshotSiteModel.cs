namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents a named site's host in the runner's inventory.</summary>
public sealed record SnapshotSiteModel
{
    /// <summary>Gets the site's name.</summary>
    public required string Site { get; init; }

    /// <summary>Gets the host name.</summary>
    public required string Host { get; init; }
}
