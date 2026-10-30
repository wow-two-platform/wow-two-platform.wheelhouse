using System.Security.Cryptography;
using System.Text;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;

namespace Wheelhouse.Application.SecretsVault.Services;

/// <summary>The built-in vault's envelope scheme over the SDK's AES-GCM <see cref="IValueCipher"/>: the root key wraps each
/// product's data key, bound to the product; a data key encrypts each secret value, bound to its product, environment,
/// key and version, so no ciphertext decrypts in another place. Holds the contexts and the fingerprint labels every
/// stored payload and fingerprint was made with; they never change, or what the vault holds no longer opens.</summary>
public static class SecretsVaultCrypto
{
    /// <summary>The length of a root or data key, in bytes.</summary>
    public const int KeyBytes = 32;

    /// <summary>The label a root key's fingerprint is hashed with; a new scheme takes a new label.</summary>
    public const string RootKeyLabel = "wheelhouse.vault.root-key.v1";

    /// <summary>The HKDF purpose a value fingerprint's HMAC key is derived from the data key with.</summary>
    public const string FingerprintPurpose = "wheelhouse-vault/fingerprint";

    /// <summary>The associated data of the canary's data key.</summary>
    public const string CanaryKeyContext = "wheelhouse-vault/canary-key";

    /// <summary>The associated data of the canary value.</summary>
    public const string CanaryContext = "wheelhouse-vault/canary";

    /// <summary>Names a root key without revealing it (<c>aeb1 80f7 9014 1a39 9680</c>); the owner's offline-copy command
    /// (deployment.md § Checking the root key) prints the same text from their copy.</summary>
    public static readonly KeyFingerprintHasher RootKeyFingerprint = new(RootKeyLabel);

    /// <summary>A value's fingerprint under its product's data key: 16 hex characters of an HMAC-SHA256 keyed by a key
    /// derived from the data key, so equal values show equal fingerprints without letting anyone test a guess.</summary>
    public static readonly ValueFingerprintHasher ValueFingerprint = new(FingerprintPurpose);

    /// <summary>The associated data binding a product's data key to the product.</summary>
    /// <param name="productId">The product's identifier.</param>
    /// <returns>The associated data.</returns>
    public static string ProductKeyContext(Guid productId) => "wheelhouse-vault/product-key/" + productId.ToString("D");

    /// <summary>The associated data binding a secret value to its place and version.</summary>
    /// <param name="productId">The product's identifier.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="key">The secret's key.</param>
    /// <param name="version">The version.</param>
    /// <returns>The associated data.</returns>
    public static string ValueContext(Guid productId, DeploymentEnvironment environment, string key, int version) =>
        "wheelhouse-vault/" + productId.ToString("D") + "/" + environment.ToString().ToLowerInvariant() + "/" + key + "/v" + version;

    /// <summary>A fresh random data key; the caller clears it after use.</summary>
    /// <returns>32 random bytes.</returns>
    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeyBytes);

    /// <summary>A non-secret setting value's digest for a render record: 16 hex characters of its SHA-256.</summary>
    /// <param name="value">The setting's value.</param>
    /// <returns>The digest.</returns>
    public static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
