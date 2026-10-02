using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Wheelhouse.Tests.Integration.Harness;
using Wheelhouse.Tests.Integration.Support;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>
/// The constraints the EF model declares are enforced by the real database: unique slugs and key hashes, and a target
/// that holds its product and server in place. Runs on the SDK <see cref="WheelhouseTestDb"/> (Postgres container or
/// in-memory SQLite); the schema is created by EF from the entity configurations.
/// </summary>
[Collection(WheelhouseTestDbCollection.Name)]
public sealed class ConstraintTests(WheelhouseTestDb db) : IAsyncLifetime
{
    /// <inheritdoc />
    public async Task InitializeAsync() => await db.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task IntegrationKeyHash_ShouldBeRejected_WhenAnotherKeyHasIt()
    {
        await using (var ctx = db.NewContext())
        {
            ctx.IntegrationKeys.Add(InventoryRows.Key("first", "hash-1"));
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = db.NewContext();
        ctx2.IntegrationKeys.Add(InventoryRows.Key("second", "hash-1"));

        await ctx2.Invoking(ctx => ctx.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Slugs_ShouldBeRejected_WhenAnotherRowHasThem()
    {
        await using (var ctx = db.NewContext())
        {
            ctx.Products.Add(InventoryRows.Product("pilot"));
            ctx.Servers.Add(InventoryRows.Server("hel1"));
            await ctx.SaveChangesAsync();
        }

        await using (var products = db.NewContext())
        {
            products.Products.Add(InventoryRows.Product("pilot"));
            await products.Invoking(ctx => ctx.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }

        await using var servers = db.NewContext();
        servers.Servers.Add(InventoryRows.Server("hel1"));
        await servers.Invoking(ctx => ctx.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Product_ShouldStay_WhenATargetStillRunsIt()
    {
        var product = InventoryRows.Product("pilot");
        var server = InventoryRows.Server("hel1");
        await using (var ctx = db.NewContext())
        {
            ctx.Products.Add(product);
            ctx.Servers.Add(server);
            ctx.Targets.Add(InventoryRows.Target("pilot-prod", product, server));
            await ctx.SaveChangesAsync();
        }

        await using var remove = db.NewContext();
        remove.Products.Remove(await remove.Products.SingleAsync(row => row.Slug == "pilot"));

        await remove.Invoking(ctx => ctx.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }
}
