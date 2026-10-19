using System.Security.Cryptography;
using System.Text;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;

namespace Wheelhouse.Application.SecretsVault.Services;

/// <summary>The built-in vault's envelope encryption, over the SDK's AES-GCM cipher: the root key wraps each product's
/// data key, bound to the product; a data key encrypts each secret value, bound to its product, environment, key and
/// version, so no ciphertext decrypts in another place. Fingerprints are HMACs under a key derived from the product's
/// data key, so equal values show equal fingerprints without letting anyone test a guess. Never logs.</summary>
public sealed class SecretsVaultCrypto(IValueCipher cipher)
{
    /// <summary>The length of a root or data key, in bytes.</summary>
    public const int KeyBytes = 32;

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

    /// <summary>The associated data of the canary's data key.</summary>
    public const string CanaryKeyContext = "wheelhouse-vault/canary-key";

    /// <summary>The associated data of the canary value.</summary>
    public const string CanaryContext = "wheelhouse-vault/canary";

    /// <summary>A fresh random data key; the caller clears it after use.</summary>
    /// <returns>32 random bytes.</returns>
    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeyBytes);

    /// <summary>Encrypts bytes under a key, bound to <paramref name="context"/>.</summary>
    /// <param name="plaintext">What to encrypt.</param>
    /// <param name="key">The key.</param>
    /// <param name="context">The associated data.</param>
    /// <returns>The ciphertext with its nonce and tag.</returns>
    public EncryptedPayload Seal(byte[] plaintext, byte[] key, string context) => cipher.Encrypt(plaintext, key, context);

    /// <summary>Decrypts bytes sealed under a key and context; throws when either differs or the bytes changed.</summary>
    /// <param name="payload">The ciphertext with its nonce and tag.</param>
    /// <param name="key">The key.</param>
    /// <param name="context">The associated data.</param>
    /// <returns>The plaintext; the caller clears it after use.</returns>
    /// <exception cref="CryptographicException">The key, context or bytes do not verify.</exception>
    public byte[] Open(EncryptedPayload payload, byte[] key, string context) => cipher.Decrypt(payload, key, context);

    /// <summary>Encrypts a secret value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="dataKey">The product's data key.</param>
    /// <param name="context">The value's associated data (<see cref="ValueContext"/>).</param>
    /// <returns>The ciphertext with its nonce and tag.</returns>
    public EncryptedPayload SealValue(string value, byte[] dataKey, string context)
    {
        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            return cipher.Encrypt(plaintext, dataKey, context);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Decrypts a secret value.</summary>
    /// <param name="payload">The ciphertext with its nonce and tag.</param>
    /// <param name="dataKey">The product's data key.</param>
    /// <param name="context">The value's associated data.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CryptographicException">The key, context or bytes do not verify.</exception>
    public string OpenValue(EncryptedPayload payload, byte[] dataKey, string context)
    {
        var plaintext = cipher.Decrypt(payload, dataKey, context);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>A value's fingerprint under its product's data key: 16 hex characters of an HMAC-SHA256 keyed by a key
    /// derived from the data key.</summary>
    /// <param name="value">The value.</param>
    /// <param name="dataKey">The product's data key.</param>
    /// <returns>The fingerprint.</returns>
    public static string Fingerprint(string value, byte[] dataKey)
    {
        var macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, dataKey, KeyBytes, info: "wheelhouse-vault/fingerprint"u8.ToArray());
        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexStringLower(HMACSHA256.HashData(macKey, plaintext))[..16];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(macKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>A non-secret setting value's digest for a render record: 16 hex characters of its SHA-256.</summary>
    /// <param name="value">The setting's value.</param>
    /// <returns>The digest.</returns>
    public static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
