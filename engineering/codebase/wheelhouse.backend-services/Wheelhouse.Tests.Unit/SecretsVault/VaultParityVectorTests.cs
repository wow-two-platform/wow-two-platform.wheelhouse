using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Wheelhouse.Application.SecretsVault.Constants;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using Wheelhouse.Domain.SecretsVault.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Tests.Unit.Fakes;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.SecretsVault;

/// <summary>
/// Byte parity with the secrets prod and dev already hold. On 2026-10-30, before the vault moved onto the be sdk's master
/// key types, Wheelhouse's own <c>SecretsVaultService</c>, <c>SecretsVaultCrypto</c> and <c>FileVaultKeySource</c>
/// (be sdk <c>10.0.94-beta</c>) staged these rows under a known root key: the canary, a product's wrapped data key, and
/// three secret versions with their fingerprints. The vault must prove the key against that canary, open every value and
/// reproduce every fingerprint exactly, or a moved console could no longer read what it wrote.
/// </summary>
public sealed class VaultParityVectorTests
{
    /// <summary>The test root key: the bytes 0 to 31.</summary>
    private static readonly byte[] Root = [.. Enumerable.Range(0, 32).Select(value => (byte)value)];

    private static readonly Guid Product = Guid.Parse("6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b");

    private readonly InMemorySecretsVaultStore _store = new();

    public VaultParityVectorTests()
    {
        _store.Add(new VaultCanaryEntity
        {
            WrappedKey = B("/H3oRr9e4fCWoM4QgGj1NmLDEWnWu8ymnz31MYi951Q="), KeyNonce = B("API55coc6H4yU+tw"),
            KeyTag = B("lVir3hO9CzyUestX/+awTA=="), CipherText = B("SWo51p9HKMspw5Hu+42hfgiUqih6gJ8Sy8ru+Wlv/r0="),
            Nonce = B("kzGIwRtGKjp6z4cJ"), Tag = B("/c4O5TkhTQNZXJIzQBEFmQ=="),
            Digest = B("QId4WQ04h6/pQDO9425JzzGSmLn7TtmVKwJa6PGM2ww="),
        });
        _store.Add(new VaultProductKeyEntity
        {
            ProductId = Product, WrappedKey = B("mrg4U5Ew7LPurgJyK0Nr5ebwc7GjS+3wUEsOTWfKgPw="), Nonce = B("pQVdiT18rYPnkSaK"),
            Tag = B("80XVLEoA3SmJI9w8dzGaUA=="),
        });
        Secret("01a18d67-0329-705b-a7bc-50c8ca2b1627", DeploymentEnvironment.Prod, "Billing:SecretKey",
            "FIHJZmfxX/oJ5O/jhfE/dA==", "Vc42p3BqFDnwenp7", "pmmRQLJOC9JCIOX3f89FpQ==", "2410c3e08e79e667");
        Secret("01a18d67-032a-7cb9-a734-b81d653fdf85", DeploymentEnvironment.Prod, "Smtp:Password",
            "", "B8bGoPNGuAiXT5tY", "T0OlXHyUpMdPVe3B5B0O0Q==", "4b8710450ed43fa4");
        Secret("01a18d67-032a-7855-ab86-3608f45f1027", DeploymentEnvironment.Dev, "Billing:SecretKey",
            "FG67FZe+Zm3WeN4=", "5FspWTEauUfADLWH", "vF2WEnWI8ccbrk4CK83Pvw==", "d0c04208bfdc195c");
    }

    [Fact]
    public async Task TheStoredCanary_ShouldProveTheRootKey_AndRefuseAnotherKey()
    {
        var other = (byte[])Root.Clone();
        other[31] ^= 1;

        (await Vault(Root).UnsealAsync(CancellationToken.None)).Reason.Should().BeNull();
        (await Vault(other).UnsealAsync(CancellationToken.None)).Should().Be(((byte[]?)null, SecretsVaultStates.Mismatch));
    }

    [Fact]
    public async Task TheStoredValues_ShouldOpenWithTheFingerprintsTheyWereWrittenWith()
    {
        var vault = Vault(Root);

        var prod = await vault.ReadAsync(Product, DeploymentEnvironment.Prod, ["Billing:SecretKey", "Smtp:Password"], CancellationToken.None);
        var dev = await vault.ReadAsync(Product, DeploymentEnvironment.Dev, ["Billing:SecretKey"], CancellationToken.None);

        prod.Should().BeOfType<AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Success>().Subject.Data
            .Select(pair => (pair.Key, pair.Value.Value, pair.Value.Version, pair.Value.Fingerprint))
            .Should().Equal(("Billing:SecretKey", "s3cr3t-välue/Ω", 1, "2410c3e08e79e667"), ("Smtp:Password", "", 1, "4b8710450ed43fa4"));
        dev.Should().BeOfType<AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Success>().Subject.Data["Billing:SecretKey"]
            .Value.Should().Be("whsec_dev_1");
    }

    [Fact]
    public async Task TheFullCheck_ShouldOpenEverythingAndReproduceEveryFingerprint()
    {
        var check = await Vault(Root).VerifyAsync(new Dictionary<Guid, string> { [Product] = "pilot" }, CancellationToken.None);

        (check.State, check.Canary, check.Products, check.Secrets, check.Versions, check.Failed)
            .Should().Be((SecretsVaultStates.Unsealed, true, 1, 3, 3, 0));
    }

    [Fact]
    public void TheRootKeyFingerprint_ShouldBeTheOneTheConsoleShowed()
    {
        SecretsVaultCrypto.RootKeyFingerprint.Hash(Root).Should().Be("aeb1 80f7 9014 1a39 9680");
    }

    private SecretsVaultService Vault(byte[] root) =>
        SecretsVaultServiceTests.Vault(_store, new FixedMasterKeyBroker(root), new MasterKeyProofTracker(), new FakeTimeProvider());

    private void Secret(string id, DeploymentEnvironment environment, string key, string cipherText, string nonce, string tag, string fingerprint)
    {
        var secret = new VaultSecretEntity { Id = Guid.Parse(id), ProductId = Product, Environment = environment, Key = key, CurrentVersion = 1 };
        _store.Add(secret);
        _store.Add(new VaultSecretVersionEntity
        {
            Id = Guid.NewGuid(), SecretId = secret.Id, Version = 1, CipherText = B(cipherText), Nonce = B(nonce), Tag = B(tag),
            Fingerprint = fingerprint, CreatedBy = "owner",
        });
    }

    private static byte[] B(string base64) => Convert.FromBase64String(base64);
}
