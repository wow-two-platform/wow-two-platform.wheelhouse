using WoW.Two.Sdk.Beta.Be.Foundation.Identifiers;
using WoW.Two.Sdk.Beta.Be.Foundation.Identifiers.Generators;

namespace Wheelhouse.Application.Deployments.Settings.Services;

/// <summary>Names the formats a secret's generated value comes in and generates one through the SDK
/// <see cref="IIdGenerator"/>: unbiased characters from the operating system's cryptographic random source, as lowercase
/// hex (64 characters, 256 bits) or unpadded base64url (43 characters, 258 bits). The value goes straight to the vault;
/// nothing returns, logs or audits it.</summary>
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
}
