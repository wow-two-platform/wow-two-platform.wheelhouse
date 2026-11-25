using System.Security.Cryptography;
using System.Text.Json;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Writes an imported ForeverPin release bundle for real-runner tests.</summary>
internal static class ForeverPinReleaseFixture
{
    public static void Import(string root, string bundle, string release)
    {
        var image = "ghcr.io/sulton-max/10x-venture-forever-pin/management@sha256:" + new string('a', 64);
        var folder = Directory.CreateDirectory(Path.Combine(root, "bundles", bundle)).FullName;
        var compose = JsonSerializer.SerializeToUtf8Bytes(new
        {
            services = new
            {
                management = new
                {
                    image, platform = "linux/amd64",
                    healthcheck = new { test = new[] { "CMD", "true" } },
                },
            },
        });
        File.WriteAllBytes(Path.Combine(folder, "compose.json"), compose);
        File.WriteAllText(Path.Combine(folder, "release.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, product = "foreverpin", release, kind = "release", sourceCommit = new string('a', 40),
            platform = "linux/amd64", rollbackCompatible = false,
            composeSha256 = Convert.ToHexStringLower(SHA256.HashData(compose)),
            images = new { management = image },
        }));
    }
}
