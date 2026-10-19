using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.SecretsVault.Entities;

/// <summary>Represents one value a secret has held: encrypted under its product's data key, never changed once written.
/// Who wrote it, when, a keyed fingerprint and an optional note make the history an audit trail without values.</summary>
public sealed class VaultSecretVersionEntity : IKeyedEntity<Guid>, IHasTableName, ICreationAuditable
{
    /// <summary>Gets the storage table name, in the <c>vault</c> schema.</summary>
    public static string TableName => "vault_secret_versions";

    /// <summary>Gets or sets the version's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the secret it belongs to.</summary>
    public Guid SecretId { get; set; }

    /// <summary>Gets or sets its number: 1 for the first value, one more for each change.</summary>
    public int Version { get; set; }

    /// <summary>Gets or sets the encrypted value.</summary>
    public byte[] CipherText { get; set; } = [];

    /// <summary>Gets or sets the value's nonce.</summary>
    public byte[] Nonce { get; set; } = [];

    /// <summary>Gets or sets the value's tag.</summary>
    public byte[] Tag { get; set; } = [];

    /// <summary>Gets or sets a keyed fingerprint of the value: equal values of a product give equal fingerprints, and
    /// nobody without the product's key can test a guess against it.</summary>
    public required string Fingerprint { get; set; }

    /// <summary>Gets or sets the owner's note on the change, such as why a key was rotated.</summary>
    public string? Note { get; set; }

    /// <summary>Gets or sets where the value came from when the owner did not type it: <c>restored from v2</c>, or the
    /// standalone vault it was imported from.</summary>
    public string? Origin { get; set; }

    /// <summary>Gets or sets who wrote it.</summary>
    public required string CreatedBy { get; set; }

    /// <summary>Gets or sets when it was written; stamped by the SDK audit interceptor unless an import keeps the
    /// original time.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
