using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Infrastructure.GitHub;

namespace Wheelhouse.Tests.Unit.GitHub;

/// <summary>Tests for <see cref="GitHubAppJwt"/>: the App token's shape, times, issuer and RS256 signature.</summary>
public sealed class GitHubAppJwtTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ShouldSignAnRs256TokenTheKeysPublicHalfVerifies()
    {
        using var key = RSA.Create(2048);

        var parts = GitHubAppJwt.Create("123456", key.ExportRSAPrivateKeyPem(), Now).Split('.');

        parts.Should().HaveCount(3);
        Json(parts[0]).Should().BeEquivalentTo(new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT" });
        key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Base64Url.DecodeFromChars(parts[2]),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldBackdateTheIssueTimeAndExpireWithinTenMinutes()
    {
        using var key = RSA.Create(2048);

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(
            GitHubAppJwt.Create("123456", key.ExportPkcs8PrivateKeyPem(), Now).Split('.')[1]));

        var root = payload.RootElement;
        root.GetProperty("iat").GetInt64().Should().Be(Now.AddSeconds(-60).ToUnixTimeSeconds());
        root.GetProperty("exp").GetInt64().Should().Be(Now.AddMinutes(9).ToUnixTimeSeconds());
        (root.GetProperty("exp").GetInt64() - root.GetProperty("iat").GetInt64()).Should().BeLessThanOrEqualTo(600);
        root.GetProperty("iss").ValueKind.Should().Be(JsonValueKind.Number, "a numeric App ID is sent as a number");
        root.GetProperty("iss").GetInt64().Should().Be(123456);
    }

    [Fact]
    public void Create_ShouldSendAClientIdAsAString()
    {
        using var key = RSA.Create(2048);

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(
            GitHubAppJwt.Create("Iv23liExample", key.ExportRSAPrivateKeyPem(), Now).Split('.')[1]));

        payload.RootElement.GetProperty("iss").GetString().Should().Be("Iv23liExample");
    }

    [Fact]
    public void Create_ShouldRefuseAFileThatHoldsNoKey()
    {
        var act = () => GitHubAppJwt.Create("123456", "not a key", Now);

        act.Should().Throw<ArgumentException>();
    }

    private static Dictionary<string, object> Json(string part) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Base64Url.DecodeFromChars(part))!
            .ToDictionary(pair => pair.Key, pair => (object)pair.Value.GetString()!);
}
