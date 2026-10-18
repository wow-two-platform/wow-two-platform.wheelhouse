using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents the release auto-promote last tried on one prod target; each release is tried once.</summary>
public sealed class AutoPromotionEntity : IHasTableName
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "auto_promotions";

    /// <summary>Gets or sets the prod target; the row's key.</summary>
    public Guid TargetId { get; set; }

    /// <summary>Gets or sets the release label last tried.</summary>
    public required string Release { get; set; }

    /// <summary>Gets or sets when it was queued.</summary>
    public DateTimeOffset AttemptedAt { get; set; }
}
