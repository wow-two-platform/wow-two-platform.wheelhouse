using System.Security.Cryptography;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>Generates a secret's value on the server: 32 bytes from the operating system's cryptographic random source,
/// written as lowercase hex (64 characters) or unpadded base64url (43 characters). The value goes straight to the vault;
/// nothing returns, logs or audits it.</summary>
public static class SecretValues
{
    /// <summary>Lowercase hexadecimal: <c>0-9a-f</c>, the default.</summary>
    public const string Hex = "hex";

    /// <summary>Unpadded base64url: <c>A-Z a-z 0-9 - _</c>, safe in URLs, headers and file names.</summary>
    public const string Base64Url = "base64url";

    /// <summary>How many random bytes a generated value carries: 256 bits.</summary>
    public const int Bytes = 32;

    /// <summary>The formats a generated value comes in.</summary>
    public static readonly IReadOnlyList<string> Formats = [Hex, Base64Url];

    /// <summary>Whether a format is one Wheelhouse generates.</summary>
    /// <param name="format">The format's name.</param>
    /// <returns><c>true</c> for <see cref="Hex"/> or <see cref="Base64Url"/>.</returns>
    public static bool IsFormat(string? format) => format is Hex or Base64Url;

    /// <summary>Generates a value.</summary>
    /// <param name="format"><see cref="Hex"/> or <see cref="Base64Url"/>.</param>
    /// <returns>The value; the caller writes it to the vault and drops it.</returns>
    public static string Generate(string format)
    {
        Span<byte> bytes = stackalloc byte[Bytes];
        RandomNumberGenerator.Fill(bytes);
        try
        {
            return format switch
            {
                Hex => Convert.ToHexStringLower(bytes),
                Base64Url => System.Buffers.Text.Base64Url.EncodeToString(bytes),
                _ => throw new ArgumentOutOfRangeException(nameof(format), "Unknown secret format."),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
