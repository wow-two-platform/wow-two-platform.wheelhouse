using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.SecretsVault.Entities;

/// <summary>Represents the owner marking their offline copy of the root key verified: they compared its fingerprint with
/// the key in use's. Only the newest counts, and only while the key in use still has the fingerprint it names.</summary>
public sealed class VaultRootKeyVerificationEntity : IHasTableName
{
    /// <summary>Gets the storage table name, in the <c>vault</c> schema, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "vault_root_key_verifications";

    /// <summary>Gets or sets the verification's identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the fingerprint of the key in use the owner compared against; never the key.</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>Gets or sets who marked it.</summary>
    public string VerifiedBy { get; set; } = "";

    /// <summary>Gets or sets how the owner says they checked it.</summary>
    public string Reason { get; set; } = "";

    /// <summary>Gets or sets when it was marked.</summary>
    public DateTimeOffset VerifiedAt { get; set; }
}
