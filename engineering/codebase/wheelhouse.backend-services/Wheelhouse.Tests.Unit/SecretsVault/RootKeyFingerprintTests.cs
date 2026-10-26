using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.SecretsVault.Services;

namespace Wheelhouse.Tests.Unit.SecretsVault;

/// <summary>Tests for <see cref="RootKeyFingerprint"/>: the owner's offline-copy command (deployment.md § Checking the root
/// key) and the console print the same text for the same key, the key's plain hash is not it, and another label gives
/// another fingerprint.</summary>
public sealed class RootKeyFingerprintTests
{
    /// <summary>A key nobody uses: the bytes 0 to 31.</summary>
    private static readonly byte[] Sample = [.. Enumerable.Range(0, 32).Select(value => (byte)value)];

    [Fact]
    public void AKnownKey_ShouldPrintWhatTheOwnersCommandPrints()
    {
        // The documented python3 one-liner over base64(Sample) prints this.
        RootKeyFingerprint.Compute(Sample).Should().Be("aeb1 80f7 9014 1a39 9680");
        RootKeyFingerprint.Compute(Sample).Should().MatchRegex(RootKeyFingerprint.Pattern);
    }

    [Fact]
    public void TheFingerprint_ShouldBeStable_AndDifferPerKey()
    {
        RootKeyFingerprint.Compute(Sample).Should().Be(RootKeyFingerprint.Compute((byte[])Sample.Clone()));
        var other = (byte[])Sample.Clone();
        other[31] ^= 1;
        RootKeyFingerprint.Compute(other).Should().NotBe(RootKeyFingerprint.Compute(Sample));
    }

    [Fact]
    public void TheLabel_ShouldSeparateItFromAnyOtherHashOfTheKey()
    {
        var bare = Convert.ToHexStringLower(SHA256.HashData(Sample))[..20];
        var relabelled = Convert.ToHexStringLower(SHA256.HashData([.. Encoding.ASCII.GetBytes("wheelhouse.vault.root-key.v2"), 0, .. Sample]))[..20];
        var labelled = Convert.ToHexStringLower(SHA256.HashData([.. Encoding.ASCII.GetBytes(RootKeyFingerprint.Label), 0, .. Sample]))[..20];
        var printed = RootKeyFingerprint.Compute(Sample).Replace(" ", "", StringComparison.Ordinal);
        (printed, printed == bare, printed == relabelled).Should().Be((labelled, false, false));
    }
}
