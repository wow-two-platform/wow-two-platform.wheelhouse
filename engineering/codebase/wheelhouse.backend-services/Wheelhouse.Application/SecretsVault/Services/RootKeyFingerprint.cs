using System.Security.Cryptography;
using System.Text;

namespace Wheelhouse.Application.SecretsVault.Services;

/// <summary>Names a root key without revealing it: the first 80 bits of SHA-256 over a label of its own, a zero byte and
/// the key's 32 bytes, in lowercase hex groups of four (<c>aeb1 80f7 9014 1a39 9680</c>). The label keeps it apart from
/// any other hash of the same key. The owner's offline-copy command (deployment.md § Checking the root key) prints the same
/// text from their copy, so the two compare by eye; the key itself never reaches a page, a log or a request.</summary>
public static class RootKeyFingerprint
{
    /// <summary>The label hashed before the key; a new scheme takes a new label.</summary>
    public const string Label = "wheelhouse.vault.root-key.v1";

    /// <summary>How many hex digits the fingerprint keeps.</summary>
    public const int Digits = 20;

    /// <summary>Matches a fingerprint as <see cref="Compute"/> writes it.</summary>
    public const string Pattern = "^[0-9a-f]{4}( [0-9a-f]{4}){4}$";

    /// <summary>Computes a root key's fingerprint.</summary>
    /// <param name="key">The 32-byte root key; the caller still clears it.</param>
    /// <returns>Twenty hex digits in groups of four, space-separated.</returns>
    public static string Compute(ReadOnlySpan<byte> key)
    {
        var label = Encoding.ASCII.GetBytes(Label);
        var input = new byte[label.Length + 1 + key.Length];
        try
        {
            label.CopyTo(input, 0);
            key.CopyTo(input.AsSpan(label.Length + 1));
            var hex = Convert.ToHexStringLower(SHA256.HashData(input))[..Digits];
            return string.Join(' ', Enumerable.Range(0, Digits / 4).Select(group => hex.Substring(group * 4, 4)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }
}
