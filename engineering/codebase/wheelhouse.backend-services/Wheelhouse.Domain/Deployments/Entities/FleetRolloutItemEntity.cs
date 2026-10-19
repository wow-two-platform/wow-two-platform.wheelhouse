using Wheelhouse.Domain.Deployments.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents one product of a fleet rollout: the release it promotes to one prod target, and its outcome. It
/// keeps the product and target slugs, so it outlives their removal.</summary>
public sealed class FleetRolloutItemEntity : IHasTableName
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "fleet_rollout_items";

    /// <summary>Gets or sets the fleet it belongs to.</summary>
    public Guid FleetId { get; set; }

    /// <summary>Gets or sets its place in the fleet's order, from 0.</summary>
    public int Position { get; set; }

    /// <summary>Gets or sets the product's slug.</summary>
    public required string Product { get; set; }

    /// <summary>Gets or sets the prod target's slug.</summary>
    public required string Target { get; set; }

    /// <summary>Gets or sets the catalog bundle it submits.</summary>
    public required string BundleId { get; set; }

    /// <summary>Gets or sets the release label, when known.</summary>
    public string? Release { get; set; }

    /// <summary>Gets or sets the release prod ran when the fleet started, when known.</summary>
    public string? FromRelease { get; set; }

    /// <summary>Gets or sets whether the owner typed the target ID, which prod on the local server needs.</summary>
    public bool Confirmed { get; set; }

    /// <summary>Gets or sets where it stands.</summary>
    public FleetItemStatus Status { get; set; }

    /// <summary>Gets or sets its deployment's identifier, once submitted.</summary>
    public string? DeploymentId { get; set; }

    /// <summary>Gets or sets the display-safe reason it failed or was refused, or a note on its success.</summary>
    public string? Detail { get; set; }

    /// <summary>Gets or sets when it was submitted.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Gets or sets when it ended.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
