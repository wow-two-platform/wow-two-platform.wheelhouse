using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Wheelhouse.Application.SecretsVault.Constants;
using Wheelhouse.Application.SecretsVault.Models;
using Wheelhouse.Application.SecretsVault.Services;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Tests.Unit.Fakes;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Security;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.Unit.SecretsVault;

/// <summary>
/// Tests for <see cref="SecretsVaultService"/> over an in-memory store: the root key unseals it and the canary proves the
/// key, values round-trip under each product's own data key, every write is a version with a keyed fingerprint, a
/// restore makes a new version, removal and expiry are metadata, and the full check finds a tampered version.
/// </summary>
public sealed class SecretsVaultServiceTests
{
    private static readonly Guid Product = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly InMemorySecretsVaultStore _store = new();
    private readonly FixedMasterKeyBroker _key = new(RandomNumberGenerator.GetBytes(32));
    private readonly MasterKeyProofTracker _proofs = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-19T12:00:00Z"));

    private SecretsVaultService Vault() => Vault(_store, _key, _proofs, _time);

    /// <summary>The vault over the SDK's master key types and AES-GCM cipher, as the host registers them.</summary>
    public static SecretsVaultService Vault(
        InMemorySecretsVaultStore store, IMasterKeyBroker broker, MasterKeyProofTracker proofs, TimeProvider time) =>
        new(store, broker, proofs, new MasterKeyUnsealService(broker, proofs), Cipher(), time,
            NullLogger<SecretsVaultService>.Instance);

    /// <summary>The SDK's AES-GCM cipher, as the host registers it.</summary>
    public static IValueCipher Cipher() =>
        new ServiceCollection().AddLogging().AddEnvelopeCryptography().BuildServiceProvider().GetRequiredService<IValueCipher>();

    private async Task<IReadOnlyList<SecretWritten>> WriteAsync(SecretsVaultService vault, Guid product, params SecretWrite[] writes)
    {
        var result = await vault.StageWritesAsync(product, DeploymentEnvironment.Prod, writes, "owner", null, CancellationToken.None);
        return result.Should().BeOfType<AppResult<IReadOnlyList<SecretWritten>>.Success>().Subject.Data;
    }

    private static async Task<Dictionary<string, string>> ReadAsync(SecretsVaultService vault, Guid product, params string[] keys)
    {
        var result = await vault.ReadAsync(product, DeploymentEnvironment.Prod, keys, CancellationToken.None);
        return result.Should().BeOfType<AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Success>().Subject.Data
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value);
    }

    [Fact]
    public async Task Values_ShouldRoundTrip_UnderEachProductsOwnKey_AndNeverBeStoredInTheClear()
    {
        var vault = Vault();
        await WriteAsync(vault, Product, new SecretWrite("Stripe:ApiKey", "sk_live_one"));
        await WriteAsync(vault, Other, new SecretWrite("Stripe:ApiKey", "sk_live_one"));

        (await ReadAsync(vault, Product, "Stripe:ApiKey"))["Stripe:ApiKey"].Should().Be("sk_live_one");
        _store.Keys.Should().HaveCount(2, "each product has a data key of its own");
        _store.Keys[0].WrappedKey.Should().NotEqual(_store.Keys[1].WrappedKey);
        _store.Versions.Should().OnlyContain(version => !System.Text.Encoding.UTF8.GetString(version.CipherText).Contains("sk_live"));
        _store.Versions[0].Fingerprint.Should().NotBe(_store.Versions[1].Fingerprint, "fingerprints are keyed by the product's key");
        _store.Canary.Should().NotBeNull("the first write binds the vault to its root key");
    }

    [Fact]
    public async Task EveryChange_ShouldBeAVersion_WithItsAuthorFingerprintAndNote()
    {
        var vault = Vault();
        var first = (await WriteAsync(vault, Product, new SecretWrite("Smtp:Password", "one", "initial")))[0];
        var second = (await WriteAsync(vault, Product, new SecretWrite("Smtp:Password", "two")))[0];
        var third = (await WriteAsync(vault, Product, new SecretWrite("Smtp:Password", "one")))[0];

        (first.Version, second.Version, third.Version).Should().Be((1, 2, 3));
        third.Fingerprint.Should().Be(first.Fingerprint).And.NotBe(second.Fingerprint).And.MatchRegex("^[0-9a-f]{16}$");
        var history = await vault.HistoryAsync("pilot-prod", Product, DeploymentEnvironment.Prod, "Smtp:Password", CancellationToken.None);
        history!.Versions.Select(version => (version.Version, version.Current, version.CreatedBy, version.Note))
            .Should().Equal((3, true, "owner", null), (2, false, "owner", null), (1, false, "owner", "initial"));
        (await ReadAsync(vault, Product, "Smtp:Password"))["Smtp:Password"].Should().Be("one");
    }

    [Fact]
    public async Task ARestore_ShouldWriteTheEarlierValueAsANewVersion()
    {
        var vault = Vault();
        await WriteAsync(vault, Product, new SecretWrite("A:Key", "first"));
        await WriteAsync(vault, Product, new SecretWrite("A:Key", "second"));
        var restored = await vault.StageRestoreAsync(Product, DeploymentEnvironment.Prod, "A:Key", 1, "rolled back", "owner", CancellationToken.None);
        restored.Should().BeOfType<AppResult<SecretWritten>.Success>().Subject.Data.Version.Should().Be(3);
        _store.Versions.Single(version => version.Version == 3).Origin.Should().Be("restored from v1");
        (await ReadAsync(vault, Product, "A:Key"))["A:Key"].Should().Be("first");
        (await vault.StageRestoreAsync(Product, DeploymentEnvironment.Prod, "A:Key", 9, null, "owner", CancellationToken.None))
            .Should().BeOfType<AppResult<SecretWritten>.Failure>().Which.Error.Type.Should().Be(AppErrorType.NotFound);
    }

    [Fact]
    public async Task WithoutAKey_TheVaultShouldBeSealed_ButStillListMetadata()
    {
        var vault = Vault();
        await WriteAsync(vault, Product, new SecretWrite("A:Key", "value"));
        _key.Key = null;

        (await vault.StatusAsync("builtin", CancellationToken.None)).Should().Match<VaultStatusDto>(status =>
            status.State == SecretsVaultStates.Sealed && status.Reason == SecretsVaultStates.Missing && status.Secrets == 1);
        (await vault.ReadAsync(Product, DeploymentEnvironment.Prod, ["A:Key"], CancellationToken.None))
            .Should().BeOfType<AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Failure>()
            .Which.Error.Message.Should().Contain("sealed");
        (await vault.StageWritesAsync(Product, DeploymentEnvironment.Prod, [new SecretWrite("A:Key", "x")], "owner", null, CancellationToken.None))
            .Should().BeOfType<AppResult<IReadOnlyList<SecretWritten>>.Failure>();
        (await vault.ListAsync(Product, DeploymentEnvironment.Prod, CancellationToken.None)).Keys.Should().Equal("A:Key");
        // An environment without secrets reads nothing and needs no key.
        (await vault.ReadAsync(Other, DeploymentEnvironment.Prod, ["A:Key"], CancellationToken.None))
            .Should().BeOfType<AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Success>();
    }

    [Fact]
    public async Task AnotherVaultsKey_ShouldSealAsAMismatch_AndAnEmptyVaultTakesAnyKey()
    {
        var vault = Vault();
        (await vault.UnsealAsync(CancellationToken.None)).Reason.Should().BeNull("nothing is bound yet");
        await WriteAsync(vault, Product, new SecretWrite("A:Key", "value"));
        _key.Key = RandomNumberGenerator.GetBytes(32);
        _key.Version++;

        (await vault.UnsealAsync(CancellationToken.None)).Should().Be(((byte[]?)null, SecretsVaultStates.Mismatch));
        _store.Canary = null;
        (await vault.UnsealAsync(CancellationToken.None)).Reason.Should().Be(SecretsVaultStates.Mismatch,
            "without a canary the first product key proves the root key");
    }

    [Fact]
    public async Task RemovalAndExpiry_ShouldBeMetadata_AndExpiryLevelsWarnAt30And7Days()
    {
        var vault = Vault();
        await WriteAsync(vault, Product, new SecretWrite("A:Key", "a", Expiry: new ExpiryChange(_time.GetUtcNow().AddDays(20))),
            new SecretWrite("B:Key", "b", Expiry: new ExpiryChange(_time.GetUtcNow().AddDays(6))),
            new SecretWrite("C:Key", "c", Expiry: new ExpiryChange(_time.GetUtcNow().AddHours(-1))),
            new SecretWrite("D:Key", "d", Expiry: new ExpiryChange(_time.GetUtcNow().AddDays(60))));
        (await vault.ExpiriesAsync(CancellationToken.None)).Select(item => (item.Secret.Key, item.Level))
            .Should().Equal(("C:Key", "expired"), ("B:Key", "7d"), ("A:Key", "30d"));

        (await vault.StageRemoveAsync(Product, DeploymentEnvironment.Prod, "C:Key", CancellationToken.None)).Should().Be(1);
        (await vault.StageExpiryAsync(Product, DeploymentEnvironment.Prod, "B:Key", null, CancellationToken.None)).Should().BeTrue();
        (await vault.ExpiriesAsync(CancellationToken.None)).Select(item => item.Secret.Key).Should().Equal("A:Key");
        (await vault.ListAsync(Product, DeploymentEnvironment.Prod, CancellationToken.None)).Keys.Should().BeEquivalentTo("A:Key", "B:Key", "D:Key");

        // A new value keeps the expiry; one that names an expiry replaces it; a removed secret comes back with a value.
        await WriteAsync(vault, Product, new SecretWrite("A:Key", "a2"), new SecretWrite("C:Key", "c2"));
        _store.Secrets.Single(secret => secret.Key == "A:Key").ExpiresAt.Should().Be(_time.GetUtcNow().AddDays(20));
        _store.Secrets.Single(secret => secret.Key == "C:Key").RemovedAt.Should().BeNull();
    }

    [Fact]
    public async Task TheFullCheck_ShouldDecryptEverything_AndNameATamperedVersion()
    {
        var vault = Vault();
        await WriteAsync(vault, Product, new SecretWrite("A:Key", "a"), new SecretWrite("B:Key", "b"));
        await WriteAsync(vault, Other, new SecretWrite("A:Key", "z"));
        var slugs = new Dictionary<Guid, string> { [Product] = "pilot", [Other] = "other" };

        var clean = await vault.VerifyAsync(slugs, CancellationToken.None);
        (clean.State, clean.Canary, clean.Products, clean.Secrets, clean.Versions, clean.Failed)
            .Should().Be((SecretsVaultStates.Unsealed, true, 2, 3, 3, 0));
        _store.Versions.Single(version => version.SecretId == _store.Secrets.Single(secret => secret.Key == "B:Key").Id).CipherText[0] ^= 0xFF;
        var tampered = await vault.VerifyAsync(slugs, CancellationToken.None);
        (tampered.Versions, tampered.Failed, tampered.Failures.Single()).Should().Be((2, 1, "pilot/prod/B:Key v1"));
    }

    [Fact]
    public async Task ACiphertext_ShouldNotDecryptInAnotherPlaceOrVersion()
    {
        var vault = Vault();
        await WriteAsync(vault, Product, new SecretWrite("A:Key", "a"));
        await WriteAsync(vault, Product, new SecretWrite("B:Key", "b"));
        var a = _store.Secrets.Single(secret => secret.Key == "A:Key");
        var b = _store.Secrets.Single(secret => secret.Key == "B:Key");
        // Swap B's ciphertext for A's: bound to A's key name, it must not open as B.
        var source = _store.Versions.Single(version => version.SecretId == a.Id);
        var target = _store.Versions.Single(version => version.SecretId == b.Id);
        (target.CipherText, target.Nonce, target.Tag) = (source.CipherText, source.Nonce, source.Tag);
        (await vault.ReadAsync(Product, DeploymentEnvironment.Prod, ["B:Key"], CancellationToken.None))
            .Should().BeOfType<AppResult<IReadOnlyDictionary<string, RenderedSecret>>.Failure>();
    }
}
