using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Mints the short-lived RS256 JSON Web Token a GitHub App signs its own API calls with: finding an
/// installation and exchanging it for an installation token.</summary>
public static class GitHubAppJwt
{
    /// <summary>How far back the issue time is set, so a host clock a little ahead of GitHub's still passes.</summary>
    public static readonly TimeSpan Backdate = TimeSpan.FromSeconds(60);

    /// <summary>How long the token lives from now; GitHub refuses more than ten minutes.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(9);

    private static readonly byte[] Header = """{"alg":"RS256","typ":"JWT"}"""u8.ToArray();

    /// <summary>Creates the token.</summary>
    /// <param name="appId">The App's numeric ID (sent as a number) or its client ID.</param>
    /// <param name="privateKeyPem">The App's private key, PKCS#1 or PKCS#8 PEM.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The signed token.</returns>
    /// <exception cref="ArgumentException">The key is not a PEM RSA private key.</exception>
    /// <exception cref="CryptographicException">The key cannot sign.</exception>
    public static string Create(string appId, string privateKeyPem, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        using var payload = new MemoryStream();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteNumber("iat", (now - Backdate).ToUnixTimeSeconds());
            writer.WriteNumber("exp", (now + Lifetime).ToUnixTimeSeconds());
            if (long.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric))
                writer.WriteNumber("iss", numeric);
            else
                writer.WriteString("iss", appId);
            writer.WriteEndObject();
        }

        var signed = Base64Url.EncodeToString(Header) + "." + Base64Url.EncodeToString(payload.ToArray());
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signed + "." + Base64Url.EncodeToString(signature);
    }
}
