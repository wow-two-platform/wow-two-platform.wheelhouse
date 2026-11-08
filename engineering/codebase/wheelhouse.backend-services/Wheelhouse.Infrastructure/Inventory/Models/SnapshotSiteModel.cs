using System.Text.Json.Serialization;

namespace Wheelhouse.Infrastructure.Inventory.Models;

/// <summary>Represents a named site's host in the runner's inventory.</summary>
public sealed record SnapshotSiteModel
{
    /// <summary>Gets the site's name.</summary>
    public required string Site { get; init; }

    /// <summary>Gets the host name.</summary>
    public required string Host { get; init; }

    /// <summary>Gets the extra host names the site also answers on; written only when there are any.</summary>
    [JsonPropertyName("aliases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Aliases { get; init; }
}
