using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.SecretsVault.Entities;

/// <summary>Represents the vault's canary: a random value encrypted under a data key of its own, which the root key
/// wraps. Decrypting it proves the root key file belongs to this vault, before and after a restore. One row.</summary>
public sealed class VaultCanaryEntity : IHasTableName, ICreationAuditable
{
    /// <summary>Gets the storage table name, in the <c>vault</c> schema, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "vault_canary";

    /// <summary>Gets or sets the row's identifier; always 1.</summary>
    public int Id { get; set; } = 1;

    /// <summary>Gets or sets the canary's data key, wrapped by the root key.</summary>
    public byte[] WrappedKey { get; set; } = [];

    /// <summary>Gets or sets the nonce that wrapped the data key.</summary>
    public byte[] KeyNonce { get; set; } = [];

    /// <summary>Gets or sets the tag that wrapped the data key.</summary>
    public byte[] KeyTag { get; set; } = [];

    /// <summary>Gets or sets the canary value's ciphertext.</summary>
    public byte[] CipherText { get; set; } = [];

    /// <summary>Gets or sets the canary value's nonce.</summary>
    public byte[] Nonce { get; set; } = [];

    /// <summary>Gets or sets the canary value's tag.</summary>
    public byte[] Tag { get; set; } = [];

    /// <summary>Gets or sets the SHA-256 of the canary value, which a decryption must reproduce.</summary>
    public byte[] Digest { get; set; } = [];

    /// <summary>Gets or sets when the canary was made; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
