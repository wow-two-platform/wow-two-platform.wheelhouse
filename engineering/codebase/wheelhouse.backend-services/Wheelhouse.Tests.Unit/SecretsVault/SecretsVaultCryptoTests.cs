using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.SecretsVault.Services;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Tests.Unit.SecretsVault;

/// <summary>Tests for <see cref="SecretsVaultCrypto"/>: values bind to their place and version, fingerprints are keyed,
/// and the standalone vault's envelope (the SDK cipher, <c>dek:{ns}</c> and <c>{ns}/{key}</c>) still opens, so an import
/// under the same master key reads what it stored.</summary>
public sealed class SecretsVaultCryptoTests
{
    private readonly SecretsVaultCrypto _crypto = SecretsVaultServiceTests.Crypto();
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void AValue_ShouldOpenOnlyInItsOwnPlaceAndVersion()
    {
        var key = SecretsVaultCrypto.NewKey();
        var context = SecretsVaultCrypto.ValueContext(Product, DeploymentEnvironment.Prod, "Stripe:ApiKey", 2);
        var sealedValue = _crypto.SealValue("sk_live_x", key, context);
        _crypto.OpenValue(sealedValue, key, context).Should().Be("sk_live_x");
        foreach (var other in new[]
                 {
                     SecretsVaultCrypto.ValueContext(Product, DeploymentEnvironment.Dev, "Stripe:ApiKey", 2),
                     SecretsVaultCrypto.ValueContext(Product, DeploymentEnvironment.Prod, "Stripe:ApiKey", 1),
                     SecretsVaultCrypto.ValueContext(Guid.NewGuid(), DeploymentEnvironment.Prod, "Stripe:ApiKey", 2),
                 })
            FluentActions.Invoking(() => _crypto.OpenValue(sealedValue, key, other)).Should().Throw<CryptographicException>();
        FluentActions.Invoking(() => _crypto.OpenValue(sealedValue, SecretsVaultCrypto.NewKey(), context)).Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Fingerprints_ShouldBeStablePerKey_AndDifferAcrossKeys()
    {
        var key = SecretsVaultCrypto.NewKey();
        SecretsVaultCrypto.Fingerprint("value", key).Should().Be(SecretsVaultCrypto.Fingerprint("value", key)).And.HaveLength(16);
        SecretsVaultCrypto.Fingerprint("value", key).Should().NotBe(SecretsVaultCrypto.Fingerprint("value", SecretsVaultCrypto.NewKey()));
        SecretsVaultCrypto.Fingerprint("value", key).Should().NotBe(SecretsVaultCrypto.Digest("value"), "a fingerprint is no plain hash");
    }

    [Fact]
    public void TheStandaloneVaultsEnvelope_ShouldOpenUnderItsMasterKey()
    {
        // As the standalone vault's SetSecret stored it: the namespace's data key wrapped under the master key with
        // `dek:{ns}`, the value under the data key with `{ns}/{key}`.
        var master = RandomNumberGenerator.GetBytes(32);
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var wrapped = _crypto.Seal(dataKey, master, "dek:foreverpin-prod");
        var value = _crypto.Seal(Encoding.UTF8.GetBytes("whsec_live"), dataKey, "foreverpin-prod/Billing:WebhookSecret");

        var opened = _crypto.Open(wrapped, master, "dek:foreverpin-prod");
        opened.Should().Equal(dataKey);
        _crypto.OpenValue(value, opened, "foreverpin-prod/Billing:WebhookSecret").Should().Be("whsec_live");
        (wrapped.Nonce.Length, wrapped.Tag.Length).Should().Be((12, 16));
    }
}
