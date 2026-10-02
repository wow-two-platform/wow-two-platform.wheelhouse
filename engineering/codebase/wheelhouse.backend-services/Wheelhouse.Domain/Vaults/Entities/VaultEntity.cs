using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Domain.Vaults.Entities;

/// <summary>Represents a secrets vault Wheelhouse administers. Its administrator password never lives here: it is a
/// file the operator places on the control host.</summary>
public sealed record VaultEntity : IKeyedEntity<Guid>, IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "vaults";

    /// <summary>Gets or sets the vault's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name routes and the credential folder know the vault by; fixed once the vault
    /// exists.</summary>
    public required string Slug { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the server the vault runs on.</summary>
    public Guid ServerId { get; set; }

    /// <summary>Gets or sets the private management endpoint Wheelhouse reaches; the browser never chooses it.</summary>
    public required string Url { get; set; }

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
