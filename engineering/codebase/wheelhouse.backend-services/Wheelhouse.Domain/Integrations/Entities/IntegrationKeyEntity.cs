using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Integrations.Entities;

/// <summary>Represents a key another program presents to read Wheelhouse — an app, a script or an agent. Only the
/// secret's hash and a display prefix are kept; the secret is shown once, when the key is created.</summary>
public sealed record IntegrationKeyEntity : IKeyedEntity<Guid>, IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "integration_keys";

    /// <summary>Gets or sets the key's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name the owner gave the key, carried as the caller's name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the secret's first characters, enough to recognize the key without revealing it.</summary>
    public required string Prefix { get; set; }

    /// <summary>Gets or sets the lowercase hex SHA-256 of the secret.</summary>
    public required string Hash { get; set; }

    /// <summary>Gets or sets the granted scopes, space-separated, such as <c>catalog:read</c>.</summary>
    public required string Scopes { get; set; }

    /// <summary>Gets or sets the owner who created the key.</summary>
    public required string CreatedBy { get; set; }

    /// <summary>Gets or sets when the key last authenticated a request.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Gets or sets when the key was revoked; a revoked key never authenticates again.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Gets or sets when the key was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets the granted scopes as a list.</summary>
    public IReadOnlyList<string> ScopeList => Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
