using Wheelhouse.Domain.Pins.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Pins.Entities;

/// <summary>Represents a pin: a workspace, product or service the owner keeps one click away, in the owner's own
/// order. A preference, not inventory: it goes with what it points at and never reaches the audit trail.</summary>
public sealed record PinEntity : IKeyedEntity<Guid>, IHasTableName, ICreationAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "pins";

    /// <summary>Gets or sets the pin's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the login of the owner the pin belongs to.</summary>
    public required string Owner { get; set; }

    /// <summary>Gets or sets what the pin points at.</summary>
    public PinKind Kind { get; set; }

    /// <summary>Gets or sets the pinned workspace; set for a workspace pin only.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>Gets or sets the pinned product, or the product of the pinned service; <c>null</c> for a workspace pin.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Gets or sets the pinned service's key in the product's <c>deploy.yml</c>; set for a service pin only.</summary>
    public string? Service { get; set; }

    /// <summary>Gets or sets the pin's place in its owner's order; lower comes first.</summary>
    public int Order { get; set; }

    /// <summary>Gets or sets when the pin was made; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
