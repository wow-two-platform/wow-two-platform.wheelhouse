using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.SecretsVault.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;

namespace Wheelhouse.Tests.Unit.SecretsVault;

/// <summary>Tests for <see cref="SecretsVaultCrypto.RootKeyFingerprint"/>, the SDK <see cref="KeyFingerprintHasher"/> under
/// Wheelhouse's label: the owner's offline-copy command (deployment.md § Checking the root key) and the console print the
/// same text for the same key, the key's plain hash is not it, and another label gives another fingerprint.</summary>
public sealed class RootKeyFingerprintTests
{
    /// <summary>A key nobody uses: the bytes 0 to 31.</summary>
    private static readonly byte[] Sample = [.. Enumerable.Range(0, 32).Select(value => (byte)value)];

    [Fact]
    public void AKnownKey_ShouldPrintWhatTheOwnersCommandPrints()
    {
        // The documented python3 one-liner over base64(Sample) prints this.
        SecretsVaultCrypto.RootKeyFingerprint.Hash(Sample).Should().Be("aeb1 80f7 9014 1a39 9680");
        SecretsVaultCrypto.RootKeyFingerprint.Hash(Sample).Should().MatchRegex(KeyFingerprintHasher.Pattern);
    }

    [Fact]
    public void TheFingerprint_ShouldBeStable_AndDifferPerKey()
    {
        SecretsVaultCrypto.RootKeyFingerprint.Hash(Sample).Should().Be(SecretsVaultCrypto.RootKeyFingerprint.Hash((byte[])Sample.Clone()));
        var other = (byte[])Sample.Clone();
        other[31] ^= 1;
        SecretsVaultCrypto.RootKeyFingerprint.Hash(other).Should().NotBe(SecretsVaultCrypto.RootKeyFingerprint.Hash(Sample));
    }

    [Fact]
    public void TheLabel_ShouldSeparateItFromAnyOtherHashOfTheKey()
    {
        var bare = Convert.ToHexStringLower(SHA256.HashData(Sample))[..20];
        var relabelled = Convert.ToHexStringLower(SHA256.HashData([.. Encoding.ASCII.GetBytes("wheelhouse.vault.root-key.v2"), 0, .. Sample]))[..20];
        var labelled = Convert.ToHexStringLower(SHA256.HashData([.. Encoding.ASCII.GetBytes(SecretsVaultCrypto.RootKeyLabel), 0, .. Sample]))[..20];
        var printed = SecretsVaultCrypto.RootKeyFingerprint.Hash(Sample).Replace(" ", "", StringComparison.Ordinal);
        (printed, printed == bare, printed == relabelled).Should().Be((labelled, false, false));
    }
}
