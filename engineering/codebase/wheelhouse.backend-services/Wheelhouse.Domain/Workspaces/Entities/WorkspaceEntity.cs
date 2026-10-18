using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Workspaces.Entities;

/// <summary>Represents a workspace: a named group of products, such as the platform apps or one's own ventures. Every
/// product belongs to exactly one.</summary>
public sealed record WorkspaceEntity : IKeyedEntity<Guid>, IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "workspaces";

    /// <summary>Gets or sets the workspace's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name the catalog knows the workspace by; fixed once the workspace exists.</summary>
    public required string Slug { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets one line on what the workspace holds, or <c>null</c> for none.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the workspace's place in the owner's order; lower comes first.</summary>
    public int Order { get; set; }

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
