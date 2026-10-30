using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.SecretsVault.Services;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;

namespace Wheelhouse.Tests.Unit.SecretsVault;

/// <summary>Tests for <see cref="SecretsVaultCrypto"/>'s scheme over the SDK cipher: values bind to their place and version, fingerprints are keyed,
/// and the standalone vault's envelope (the SDK cipher, <c>dek:{ns}</c> and <c>{ns}/{key}</c>) still opens, so an import
/// under the same master key reads what it stored.</summary>
public sealed class SecretsVaultCryptoTests
{
    private readonly IValueCipher _cipher = SecretsVaultServiceTests.Cipher();
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void AValue_ShouldOpenOnlyInItsOwnPlaceAndVersion()
    {
        var key = SecretsVaultCrypto.NewKey();
        var context = SecretsVaultCrypto.ValueContext(Product, DeploymentEnvironment.Prod, "Stripe:ApiKey", 2);
        var sealedValue = _cipher.EncryptText("sk_live_x", key, context);
        _cipher.DecryptText(sealedValue, key, context).Should().Be("sk_live_x");
        foreach (var other in new[]
                 {
                     SecretsVaultCrypto.ValueContext(Product, DeploymentEnvironment.Dev, "Stripe:ApiKey", 2),
                     SecretsVaultCrypto.ValueContext(Product, DeploymentEnvironment.Prod, "Stripe:ApiKey", 1),
                     SecretsVaultCrypto.ValueContext(Guid.NewGuid(), DeploymentEnvironment.Prod, "Stripe:ApiKey", 2),
                 })
            FluentActions.Invoking(() => _cipher.DecryptText(sealedValue, key, other)).Should().Throw<CryptographicException>();
        FluentActions.Invoking(() => _cipher.DecryptText(sealedValue, SecretsVaultCrypto.NewKey(), context)).Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Fingerprints_ShouldBeStablePerKey_AndDifferAcrossKeys()
    {
        var key = SecretsVaultCrypto.NewKey();
        SecretsVaultCrypto.ValueFingerprint.Hash("value", key).Should().Be(SecretsVaultCrypto.ValueFingerprint.Hash("value", key)).And.HaveLength(16);
        SecretsVaultCrypto.ValueFingerprint.Hash("value", key).Should().NotBe(SecretsVaultCrypto.ValueFingerprint.Hash("value", SecretsVaultCrypto.NewKey()));
        SecretsVaultCrypto.ValueFingerprint.Hash("value", key).Should().NotBe(SecretsVaultCrypto.Digest("value"), "a fingerprint is no plain hash");
    }

    [Fact]
    public void TheStandaloneVaultsEnvelope_ShouldOpenUnderItsMasterKey()
    {
        // As the standalone vault's SetSecret stored it: the namespace's data key wrapped under the master key with
        // `dek:{ns}`, the value under the data key with `{ns}/{key}`.
        var master = RandomNumberGenerator.GetBytes(32);
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var wrapped = _cipher.Encrypt(dataKey, master, "dek:foreverpin-prod");
        var value = _cipher.Encrypt(Encoding.UTF8.GetBytes("whsec_live"), dataKey, "foreverpin-prod/Billing:WebhookSecret");

        var opened = _cipher.Decrypt(wrapped, master, "dek:foreverpin-prod");
        opened.Should().Equal(dataKey);
        _cipher.DecryptText(value, opened, "foreverpin-prod/Billing:WebhookSecret").Should().Be("whsec_live");
        (wrapped.Nonce.Length, wrapped.Tag.Length).Should().Be((12, 16));
    }
}
