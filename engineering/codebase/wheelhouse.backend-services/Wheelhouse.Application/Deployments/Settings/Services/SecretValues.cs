using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WoW.Two.Sdk.Beta.Be.Foundation.Identifiers;
using WoW.Two.Sdk.Beta.Be.Foundation.Identifiers.Generators;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>Names the formats a secret's generated value comes in and generates one through the SDK
/// <see cref="IIdGenerator"/>: unbiased characters from the operating system's cryptographic random source, as lowercase
/// hex (64 characters, 256 bits) or unpadded base64url (43 characters, 258 bits); a release's <c>generate: { random: N }</c>
/// takes N bytes from the same source, encoded. The value goes straight to the vault; nothing returns, logs or audits it.
/// </summary>
public static class SecretValues
{
    /// <summary>Lowercase hexadecimal: <c>0-9a-f</c>, the default.</summary>
    public const string Hex = "hex";

    /// <summary>Unpadded base64url: <c>A-Z a-z 0-9 - _</c>, safe in URLs, headers and file names.</summary>
    public const string Base64Url = "base64url";

    /// <summary>The formats a generated value comes in.</summary>
    public static readonly IReadOnlyList<string> Formats = [Hex, Base64Url];

    /// <summary>Whether a format is one Wheelhouse generates.</summary>
    /// <param name="format">The format's name.</param>
    /// <returns><c>true</c> for <see cref="Hex"/> or <see cref="Base64Url"/>.</returns>
    public static bool IsFormat(string? format) => format is Hex or Base64Url;

    /// <summary>Generates a value of at least 256 bits.</summary>
    /// <param name="ids">The SDK generator.</param>
    /// <param name="format"><see cref="Hex"/> or <see cref="Base64Url"/>.</param>
    /// <returns>The value; the caller writes it to the vault and drops it.</returns>
    public static string GenerateSecret(this IIdGenerator ids, string format)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return format switch
        {
            Hex => ids.Generate(64, IdAlphabetConstants.HexLower),
            Base64Url => ids.Generate(43, IdAlphabetConstants.Base64Url),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Unknown secret format."),
        };
    }

    /// <summary>The fewest random bytes a release's <c>generate</c> asks for.</summary>
    public const int MinRandomBytes = 16;

    /// <summary>The most random bytes a release's <c>generate</c> asks for.</summary>
    public const int MaxRandomBytes = 64;

    /// <summary>Encodes <paramref name="bytes"/> random bytes from the operating system's cryptographic source, as a
    /// release's <c>generate: { random: N, encoding }</c> declares them: canonical unpadded base64url or lowercase hex, so
    /// a service decoding the value gets exactly N bytes.</summary>
    /// <param name="bytes">How many bytes, <see cref="MinRandomBytes"/> to <see cref="MaxRandomBytes"/>.</param>
    /// <param name="format"><see cref="Base64Url"/> or <see cref="Hex"/>.</param>
    /// <returns>The value; the caller writes it to the vault and drops it.</returns>
    public static string RandomBytes(int bytes, string format)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, MinRandomBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes, MaxRandomBytes);
        if (!IsFormat(format))
            throw new ArgumentOutOfRangeException(nameof(format), "Unknown secret format.");
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        try
        {
            return format == Hex ? Convert.ToHexStringLower(buffer) : System.Buffers.Text.Base64Url.EncodeToString(buffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    /// <summary>When a value holding PEM certificates stops being valid: the earliest end date (<c>notAfter</c>) of the
    /// certificates in it, so a generated key pair's version lapses with its certificate. Reads only the
    /// <c>CERTIFICATE</c> blocks; any private key beside them is never parsed.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The end date, or null when the value holds no readable certificate.</returns>
    public static DateTimeOffset? CertificateEnd(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
            return null;
        DateTimeOffset? end = null;
        var rest = value.AsSpan();
        while (PemEncoding.TryFind(rest, out var fields))
        {
            var label = rest[fields.Label];
            if (label.SequenceEqual("CERTIFICATE"))
            {
                try
                {
                    using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(rest[fields.Base64Data].ToString()));
                    var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                    if (end is null || notAfter < end)
                        end = notAfter;
                }
                catch (Exception exception) when (exception is CryptographicException or FormatException)
                {
                    // An unreadable block sets no expiry; the readable ones still do.
                }
            }
            rest = rest[fields.Location.End..];
        }
        return end;
    }
}
