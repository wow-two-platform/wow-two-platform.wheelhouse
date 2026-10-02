using AwesomeAssertions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Wheelhouse.Persistence.Repositories;
using Wheelhouse.Tests.Integration.Harness;
using Wheelhouse.Tests.Integration.Support;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>
/// The Wheelhouse repositories over a real relational database: slug lookups, listing order, the guards deletes rely on,
/// the JSON columns that hold release sources, ingress and target details, and the API key lookups. Runs on the SDK
/// <see cref="WheelhouseTestDb"/> (Postgres container or in-memory SQLite).
/// </summary>
[Collection(WheelhouseTestDbCollection.Name)]
public sealed class RepositoryTests(WheelhouseTestDb db) : IAsyncLifetime
{
    /// <inheritdoc />
    public async Task InitializeAsync() => await db.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Products_ShouldListBySlugAndFindOneTracked_WhenStored()
    {
        await using (var ctx = db.NewContext())
        {
            var products = new ProductsRepository(ctx);
            foreach (var slug in new[] { "zeta", "alpha", "mid" })
                await products.CreateAsync(InventoryRows.Product(slug));
        }

        await using var read = db.NewContext();
        var repository = new ProductsRepository(read);
        (await repository.GetAllAsync()).Select(product => product.Slug).Should().Equal("alpha", "mid", "zeta");
        var found = await repository.GetBySlugAsync("mid");
        found!.Repository.Should().Be("owner/mid");
        read.Entry(found).State.Should().Be(EntityState.Unchanged);
        (await repository.GetBySlugAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task ProductRelease_ShouldRoundTripAsCamelCaseJson_WhenStored()
    {
        var stored = InventoryRows.Product("pilot");
        await using (var ctx = db.NewContext())
            await new ProductsRepository(ctx).CreateAsync(stored);

        await using var read = db.NewContext();
        var loaded = await new ProductsRepository(read).GetBySlugAsync("pilot");
        loaded!.Release!.Images.Should().BeEquivalentTo(stored.Release!.Images);
        (loaded.Release.Asset, loaded.Release.Workflow).Should().Be(("pilot-release.tar.gz", "publish.yml"));
        var raw = await read.Database.GetDbConnection()
            .ExecuteScalarAsync<string>("select cast(release as text) from products where slug = 'pilot'");
        raw.Should().Contain("\"asset\"").And.Contain("\"images\"");
    }

    [Fact]
    public async Task ServerIngressAndTargetDetails_ShouldRoundTrip_WhenStored()
    {
        var product = InventoryRows.Product("pilot");
        var server = InventoryRows.Server("hel1");
        var target = InventoryRows.Target("pilot-prod", product, server);
        await using (var ctx = db.NewContext())
        {
            await new ProductsRepository(ctx).CreateAsync(product);
            await new ServersRepository(ctx).CreateAsync(server);
            await new TargetsRepository(ctx).CreateAsync(target);
        }

        await using var read = db.NewContext();
        var loadedServer = await new ServersRepository(read).GetBySlugAsync("hel1");
        loadedServer!.Ingress.Should().BeEquivalentTo(server.Ingress);
        var loadedTarget = await new TargetsRepository(read).GetBySlugAsync("pilot-prod");
        loadedTarget!.Settings.Should().BeEquivalentTo(target.Settings);
        loadedTarget.SmokeChecks.Should().BeEquivalentTo(target.SmokeChecks);
        loadedTarget.Sites.Should().BeEquivalentTo(target.Sites);
    }

    [Fact]
    public async Task ChangedTargetDetails_ShouldPersist_WhenUpdated()
    {
        var product = InventoryRows.Product("pilot");
        var server = InventoryRows.Server("hel1");
        await using (var ctx = db.NewContext())
        {
            await new ProductsRepository(ctx).CreateAsync(product);
            await new ServersRepository(ctx).CreateAsync(server);
            await new TargetsRepository(ctx).CreateAsync(InventoryRows.Target("pilot-prod", product, server));
        }

        await using (var ctx = db.NewContext())
        {
            var targets = new TargetsRepository(ctx);
            var target = await targets.GetBySlugAsync("pilot-prod");
            // A replaced list is a change the JSON comparer sees, so the update writes it.
            target!.Sites = [.. target.Sites, new Wheelhouse.Domain.Targets.Models.SiteHostValueObject { Site = "go", Host = "go.example.com" }];
            await targets.UpdateAsync(target);
        }

        await using var read = db.NewContext();
        (await new TargetsRepository(read).GetBySlugAsync("pilot-prod"))!.Sites.Select(site => site.Site)
            .Should().Equal("app", "go");
    }

    [Fact]
    public async Task DeleteGuards_ShouldSeeWhatStillRunsOnAProductOrServer_WhenAsked()
    {
        var product = InventoryRows.Product("pilot");
        var idle = InventoryRows.Product("idle");
        var server = InventoryRows.Server("hel1");
        var empty = InventoryRows.Server("fsn1");
        await using (var ctx = db.NewContext())
        {
            var products = new ProductsRepository(ctx);
            await products.CreateAsync(product);
            await products.CreateAsync(idle);
            var servers = new ServersRepository(ctx);
            await servers.CreateAsync(server);
            await servers.CreateAsync(empty);
            await new TargetsRepository(ctx).CreateAsync(InventoryRows.Target("pilot-prod", product, server));
            await new VaultsRepository(ctx).CreateAsync(InventoryRows.Vault("hel1-vault", server));
        }

        await using var read = db.NewContext();
        var targets = new TargetsRepository(read);
        var vaults = new VaultsRepository(read);
        (await targets.AnyForProductAsync(product.Id), await targets.AnyForProductAsync(idle.Id)).Should().Be((true, false));
        (await targets.AnyForServerAsync(server.Id), await targets.AnyForServerAsync(empty.Id)).Should().Be((true, false));
        (await vaults.AnyForServerAsync(server.Id), await vaults.AnyForServerAsync(empty.Id)).Should().Be((true, false));
    }

    [Fact]
    public async Task LiveKeyLookup_ShouldFindOnlyAnUnrevokedKey_WhenMatchedByHash()
    {
        await using (var ctx = db.NewContext())
        {
            var keys = new IntegrationKeysRepository(ctx);
            await keys.AddAsync(InventoryRows.Key("live", "hash-live"));
            await keys.AddAsync(InventoryRows.Key("revoked", "hash-revoked") with { RevokedAt = DateTimeOffset.UtcNow });
        }

        await using var read = db.NewContext();
        var repository = new IntegrationKeysRepository(read);
        (await repository.FindLiveByHashAsync("hash-live", CancellationToken.None))!.Name.Should().Be("live");
        (await repository.FindLiveByHashAsync("hash-revoked", CancellationToken.None)).Should().BeNull();
        (await repository.FindLiveByHashAsync("hash-unknown", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task KeyTouch_ShouldWriteOnlyThatKeysLastUse_WhenRecorded()
    {
        var used = InventoryRows.Key("used", "hash-used");
        var idle = InventoryRows.Key("idle", "hash-idle");
        await using (var ctx = db.NewContext())
        {
            var keys = new IntegrationKeysRepository(ctx);
            await keys.AddAsync(used);
            await keys.AddAsync(idle);
        }

        var at = DateTimeOffset.UtcNow;
        await using (var ctx = db.NewContext())
            await new IntegrationKeysRepository(ctx).TouchAsync(used.Id.ToString(), at, CancellationToken.None);

        await using var read = db.NewContext();
        (await read.IntegrationKeys.FindAsync(used.Id))!.LastUsedAt.Should().BeCloseTo(at, TimeSpan.FromMilliseconds(1));
        (await read.IntegrationKeys.FindAsync(idle.Id))!.LastUsedAt.Should().BeNull();
    }

    [Fact]
    public async Task Keys_ShouldListNewestFirst_WhenCreatedAtDiffers()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var ctx = db.NewContext())
        {
            // Insert out of chronological order to prove the ORDER BY, not insertion order.
            ctx.IntegrationKeys.AddRange(
                InventoryRows.Key("middle", "hash-m", now.AddMinutes(-5)),
                InventoryRows.Key("newest", "hash-n", now),
                InventoryRows.Key("oldest", "hash-o", now.AddMinutes(-10)));
            await ctx.SaveChangesAsync();
        }

        await using var read = db.NewContext();
        (await new IntegrationKeysRepository(read).ListAsync()).Select(key => key.Name)
            .Should().Equal("newest", "middle", "oldest");
    }
}
