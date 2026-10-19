using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.SecretsVault.Entities;

/// <summary>Represents a product's data key: it encrypts every secret value of the product's environments, and is
/// stored only wrapped by the root key, bound to the product's identifier.</summary>
public sealed class VaultProductKeyEntity : IHasTableName, ICreationAuditable
{
    /// <summary>Gets the storage table name, in the <c>vault</c> schema.</summary>
    public static string TableName => "vault_product_keys";

    /// <summary>Gets or sets the product the key belongs to; the row's key.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the data key, wrapped by the root key.</summary>
    public byte[] WrappedKey { get; set; } = [];

    /// <summary>Gets or sets the nonce that wrapped it.</summary>
    public byte[] Nonce { get; set; } = [];

    /// <summary>Gets or sets the tag that wrapped it.</summary>
    public byte[] Tag { get; set; } = [];

    /// <summary>Gets or sets when the key was made; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
