using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.SecretsVault.Entities;

/// <summary>Represents one secret of a product environment, keyed by its configuration path. Its values live in
/// <see cref="VaultSecretVersionEntity"/> rows, encrypted; this row holds metadata only.</summary>
public sealed class VaultSecretEntity : IKeyedEntity<Guid>, IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name, in the <c>vault</c> schema.</summary>
    public static string TableName => "vault_secrets";

    /// <summary>Gets or sets the secret's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the product the secret belongs to.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the environment it applies to.</summary>
    public DeploymentEnvironment Environment { get; set; }

    /// <summary>Gets or sets the configuration path, sections joined by <c>:</c>.</summary>
    public required string Key { get; set; }

    /// <summary>Gets or sets the version a render reads; the latest one.</summary>
    public int CurrentVersion { get; set; }

    /// <summary>Gets or sets when the secret lapses, if the owner set an expiry; warnings start 30 days before.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Gets or sets when the owner removed it; a removed secret renders nowhere, and its versions stay.</summary>
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>Gets or sets when the row was created; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row last changed; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
