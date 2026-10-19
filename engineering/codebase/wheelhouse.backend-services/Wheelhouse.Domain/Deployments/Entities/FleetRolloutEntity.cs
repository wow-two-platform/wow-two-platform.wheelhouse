using Wheelhouse.Domain.Deployments.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents one fleet rollout: several products promoted to prod in one run, in order, stopping at the first
/// failure. One runs at a time; its products are <see cref="Items"/>.</summary>
public sealed class FleetRolloutEntity : IKeyedEntity<Guid>, IHasTableName
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "fleet_rollouts";

    /// <summary>Gets or sets the fleet's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets who started it; each of its deploys is theirs.</summary>
    public required string Actor { get; set; }

    /// <summary>Gets or sets the owner's one-line why, kept on every deploy it submits.</summary>
    public string? Reason { get; set; }

    /// <summary>Gets or sets whether its deploys go through a freeze; it then has a reason.</summary>
    public bool OverrideFreeze { get; set; }

    /// <summary>Gets or sets where it stands.</summary>
    public FleetRolloutStatus Status { get; set; }

    /// <summary>Gets or sets who asked to cancel it, once someone did.</summary>
    public string? CancelRequestedBy { get; set; }

    /// <summary>Gets or sets when cancelling was asked for; the fleet stops after the product it is on.</summary>
    public DateTimeOffset? CancelRequestedAt { get; set; }

    /// <summary>Gets or sets when it was started.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when it ended; null while it runs.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Gets or sets its products, in the order they are promoted.</summary>
    public List<FleetRolloutItemEntity> Items { get; set; } = [];
}
