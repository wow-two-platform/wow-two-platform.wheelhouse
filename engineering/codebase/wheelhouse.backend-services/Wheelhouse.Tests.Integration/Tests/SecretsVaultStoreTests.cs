using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.SecretsVault.Entities;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Settings.Enums;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Persistence;
using Wheelhouse.Persistence.Repositories;
using Wheelhouse.Tests.Integration.Harness;
using Wheelhouse.Tests.Integration.Support;
using WoW.Two.Sdk.Beta.Be.Data.Sessions;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>
/// The built-in vault's tables and their neighbours over a real relational database: secrets, versions and wrapped keys
/// round-trip as bytes, one secret per product environment and key, rows go with their product, a transaction that is
/// not committed keeps nothing and forgets what it staged, and the added keys, renders and scheduled applies store as the
/// console reads them. Runs on the SDK <see cref="WheelhouseTestDb"/> (Postgres container or in-memory SQLite).
/// </summary>
[Collection(WheelhouseTestDbCollection.Name)]
public sealed class SecretsVaultStoreTests(WheelhouseTestDb db) : IAsyncLifetime
{
    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        await InventoryRows.SeedWorkspaceAsync(db);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ProductEntity> StoreProductAsync(string slug)
    {
        var product = InventoryRows.Product(slug);
        await using var ctx = db.NewContext();
        await new ProductsRepository(ctx).CreateAsync(product);
        return product;
    }

    private static (VaultSecretEntity Secret, VaultSecretVersionEntity Version) Secret(Guid product, string key, int version = 1) =>
        Secret(product, key, Guid.CreateVersion7(), version);

    private static (VaultSecretEntity Secret, VaultSecretVersionEntity Version) Secret(Guid product, string key, Guid id, int version)
    {
        var secret = new VaultSecretEntity { Id = id, ProductId = product, Environment = DeploymentEnvironment.Prod, Key = key, CurrentVersion = version };
        return (secret, new VaultSecretVersionEntity
        {
            Id = Guid.CreateVersion7(), SecretId = id, Version = version, CipherText = [1, 2, 3, 0, 255], Nonce = new byte[12], Tag = new byte[16],
            Fingerprint = "00112233aabbccdd", Note = "first", CreatedBy = "owner",
        });
    }

    [Fact]
    public async Task Secrets_ShouldRoundTripTheirBytesAndVersions_ThroughTheStore()
    {
        var product = await StoreProductAsync("pilot");
        var (secret, first) = Secret(product.Id, "Stripe:ApiKey");
        await using (var ctx = db.NewContext())
        {
            var store = new SecretsVaultStore(ctx);
            store.Add(new VaultProductKeyEntity { ProductId = product.Id, WrappedKey = [9, 8, 7], Nonce = new byte[12], Tag = new byte[16] });
            store.Add(secret);
            store.Add(first);
            (await store.GetSecretAsync(product.Id, DeploymentEnvironment.Prod, "Stripe:ApiKey", default)).Should().BeSameAs(secret,
                "a lookup sees what the same save staged");
            await store.SaveAsync(default);
        }
        await using (var ctx = db.NewContext())
        {
            var store = new SecretsVaultStore(ctx);
            var tracked = (await store.GetSecretAsync(product.Id, DeploymentEnvironment.Prod, "Stripe:ApiKey", default))!;
            tracked.CurrentVersion = 2;
            tracked.ExpiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
            store.Add(new VaultSecretVersionEntity
            {
                Id = Guid.CreateVersion7(), SecretId = tracked.Id, Version = 2, CipherText = [4], Nonce = new byte[12], Tag = new byte[16],
                Fingerprint = "ffeeddccbbaa9988", CreatedBy = "owner", Origin = "restored from v1",
            });
            await store.SaveAsync(default);
        }

        await using var read = db.NewContext();
        var reader = new SecretsVaultStore(read);
        var secrets = await reader.GetSecretsAsync(product.Id, DeploymentEnvironment.Prod, default);
        secrets.Should().ContainSingle().Which.ExpiresAt.Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        (await reader.GetVersionsAsync(secret.Id, default)).Select(version => version.Version).Should().Equal(2, 1);
        var current = (await reader.GetCurrentVersionsAsync(secrets, default))[secret.Id];
        (current.Version, current.Origin, current.Fingerprint).Should().Be((2, "restored from v1", "ffeeddccbbaa9988"));
        (await reader.GetVersionAsync(secret.Id, 1, default))!.CipherText.Should().Equal(1, 2, 3, 0, 255);
        (await reader.GetProductKeyAsync(product.Id, default))!.WrappedKey.Should().Equal(9, 8, 7);
        (await reader.GetAllVersionsAsync(default)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ASecret_ShouldBeOncePerProductEnvironmentAndKey_AndGoWithItsProduct()
    {
        var product = await StoreProductAsync("pilot");
        await using (var ctx = db.NewContext())
        {
            var store = new SecretsVaultStore(ctx);
            var (secret, version) = Secret(product.Id, "A:Key");
            store.Add(secret);
            store.Add(version);
            store.Add(new VaultProductKeyEntity { ProductId = product.Id, WrappedKey = [1], Nonce = new byte[12], Tag = new byte[16] });
            await store.SaveAsync(default);
        }
        await using (var ctx = db.NewContext())
        {
            ctx.VaultSecrets.Add(Secret(product.Id, "A:Key").Secret);
            await FluentActions.Invoking(() => ctx.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
        await using (var ctx = db.NewContext())
        {
            ctx.Products.Remove(await ctx.Products.SingleAsync(row => row.Id == product.Id));
            await ctx.SaveChangesAsync();
        }
        await using var read = db.NewContext();
        (await read.VaultSecrets.CountAsync(), await read.VaultSecretVersions.CountAsync(), await read.VaultProductKeys.CountAsync())
            .Should().Be((0, 0, 0));
    }

    [Fact]
    public async Task ATransactionNotCommitted_ShouldKeepNothing_AndForgetWhatItStaged()
    {
        var product = await StoreProductAsync("pilot");
        await using (var ctx = db.NewContext())
        await using (var sessions = DataSessions(ctx))
        await using (var scope = sessions.CreateAsyncScope())
        {
            await using (await scope.ServiceProvider.GetRequiredService<IDataSession>().BeginAsync())
            {
                var store = new SecretsVaultStore(ctx);
                var (secret, version) = Secret(product.Id, "A:Key");
                store.Add(secret);
                store.Add(version);
                // The settings store saves on its own; inside the transaction that save is not yet kept.
                await new EnvironmentSettingsRepository(ctx).SetAsync(product.Id, DeploymentEnvironment.Prod,
                    new Dictionary<string, string> { ["Feature:Flag"] = "on" });
                store.Add(Secret(product.Id, "B:Key").Secret);
            }
            ctx.ChangeTracker.Entries().Should().BeEmpty("a later save on the context, such as the audit trail's, writes none of it");
        }
        await using var read = db.NewContext();
        (await read.VaultSecrets.CountAsync(), await read.EnvironmentSettings.CountAsync()).Should().Be((0, 0));

        await using (var ctx = db.NewContext())
        await using (var sessions = DataSessions(ctx))
        await using (var scope = sessions.CreateAsyncScope())
        {
            await using var unit = await scope.ServiceProvider.GetRequiredService<IDataSession>().BeginAsync();
            var store = new SecretsVaultStore(ctx);
            var (secret, version) = Secret(product.Id, "A:Key");
            store.Add(secret);
            store.Add(version);
            await new EnvironmentSettingsRepository(ctx).SetAsync(product.Id, DeploymentEnvironment.Prod,
                new Dictionary<string, string> { ["Feature:Flag"] = "on" });
            // Completing saves what the vault staged; the handlers rely on it.
            await unit.CompleteAsync();
        }
        await using var committed = db.NewContext();
        (await committed.VaultSecrets.CountAsync(), await committed.EnvironmentSettings.CountAsync()).Should().Be((1, 1));
    }

    /// <summary>The SDK data session as the host registers it, over one context the caller owns.</summary>
    private static ServiceProvider DataSessions(WheelhouseDbContext ctx) =>
        new ServiceCollection().AddSingleton(ctx).AddDataSession<WheelhouseDbContext>().BuildServiceProvider();

    [Fact]
    public async Task AddedKeys_ShouldStoreTheirTypeAndChoices_AndBeReplacedOrRemoved()
    {
        var product = await StoreProductAsync("pilot");
        await using (var ctx = db.NewContext())
        {
            var keys = new ProductSettingKeysRepository(ctx);
            await keys.SetAsync(new ProductSettingKeyEntity
            {
                ProductId = product.Id, Key = "Billing:Mode", Type = SettingType.Choice, Choices = "test\nlive", CreatedBy = "owner",
            });
            await keys.SetAsync(new ProductSettingKeyEntity
            {
                ProductId = product.Id, Key = "Billing:Mode", Type = SettingType.Choice, Choices = "test\nlive\nsandbox", Required = true,
                CreatedBy = "owner",
            });
            await keys.SetAsync(new ProductSettingKeyEntity
            {
                ProductId = product.Id, Key = "Promo:EndsOn", Type = SettingType.Date, Format = "dd.MM.yyyy", CreatedBy = "owner",
            });
        }
        await using (var ctx = db.NewContext())
        {
            var stored = await new ProductSettingKeysRepository(ctx).GetAsync(product.Id);
            stored.Select(key => (key.Key, key.Type, key.Required)).Should().Equal(
                ("Billing:Mode", SettingType.Choice, true), ("Promo:EndsOn", SettingType.Date, false));
            stored[0].Choices.Should().Be("test\nlive\nsandbox");
            (await new ProductSettingKeysRepository(ctx).RemoveAsync(product.Id, "Promo:EndsOn")).Should().BeTrue();
            (await new ProductSettingKeysRepository(ctx).RemoveAsync(product.Id, "Promo:EndsOn")).Should().BeFalse();
        }
    }

    [Fact]
    public async Task RendersAndScheduledApplies_ShouldReadAsTheConsoleNeedsThem()
    {
        var product = await StoreProductAsync("pilot");
        var server = InventoryRows.Server("pilot-host");
        var target = InventoryRows.Target("pilot-prod", product, server);
        await using (var ctx = db.NewContext())
        {
            ctx.Servers.Add(server);
            ctx.Targets.Add(target);
            await ctx.SaveChangesAsync();
            var renders = new DeploymentRendersRepository(ctx);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            await renders.AddAsync(new DeploymentRenderEntity { JobId = first, Target = "pilot-prod", Keys = "{}", At = DateTimeOffset.Parse("2026-10-19T10:00:00Z") });
            await renders.AddAsync(new DeploymentRenderEntity
            {
                JobId = second, Target = "pilot-prod", Keys = "{}", Changes = """[{"key":"A:B","kind":"setting","change":"changed"}]""",
                At = DateTimeOffset.Parse("2026-10-19T11:00:00Z"),
            });
            (await renders.GetLatestAsync("pilot-prod"))!.JobId.Should().Be(second);
            (await renders.GetChangesAsync()).Keys.Should().Equal(second.ToString());

            var applies = new SettingsAppliesRepository(ctx);
            await applies.SetAsync(new SettingsApplyEntity { TargetId = target.Id, At = DateTimeOffset.Parse("2026-10-20T03:00:00Z"), RequestedBy = "owner" });
            await applies.SetAsync(new SettingsApplyEntity { TargetId = target.Id, At = DateTimeOffset.Parse("2026-10-20T04:00:00Z"), RequestedBy = "owner" });
            (await applies.GetDueAsync(DateTimeOffset.Parse("2026-10-20T03:30:00Z"))).Should().BeEmpty("the newer schedule replaced the first");
            (await applies.GetDueAsync(DateTimeOffset.Parse("2026-10-20T04:00:00Z"))).Should().ContainSingle();
        }
    }
}
