using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using Wheelhouse.Domain.Secrets.Enums;

namespace Wheelhouse.Domain.Secrets.Entities;

/// <summary>
/// An encrypted credential Wheelhouse needs — SSH key, registrar API key, GHCR PAT, or a product env var.
/// v1 stores ciphertext locally (AES-GCM); the migration target is the wow-two secrets-vault platform service.
/// </summary>
public sealed class SecretEntry : IKeyedEntity<Guid>
{
    /// <summary>Gets the secret's unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets what the secret is scoped to.</summary>
    public SecretScope Scope { get; init; } = SecretScope.Global;

    /// <summary>Gets the id of the server/product/environment the secret belongs to, when scoped.</summary>
    public Guid? RefId { get; init; }

    /// <summary>Gets or sets the secret key/name within its scope.</summary>
    public required string Key { get; set; }

    /// <summary>Gets or sets the AES-GCM ciphertext.</summary>
    public required byte[] CipherText { get; set; }

    /// <summary>Gets or sets the AES-GCM nonce.</summary>
    public required byte[] Nonce { get; set; }

    /// <summary>Gets or sets the AES-GCM authentication tag.</summary>
    public required byte[] Tag { get; set; }

    /// <summary>Gets or sets the UTC instant the value was last written.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
