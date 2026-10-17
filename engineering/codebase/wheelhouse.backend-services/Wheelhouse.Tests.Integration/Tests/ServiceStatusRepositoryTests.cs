using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Persistence.Repositories;
using Wheelhouse.Tests.Integration.Harness;
using Wheelhouse.Tests.Integration.Support;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>
/// The service statuses store over a real relational database: one row per product and service, set again in place,
/// listed by product then service, and gone with its product.
/// </summary>
[Collection(WheelhouseTestDbCollection.Name)]
public sealed class ServiceStatusRepositoryTests(WheelhouseTestDb db) : IAsyncLifetime
{
    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        await InventoryRows.SeedWorkspaceAsync(db);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Set_ShouldAddARowOnceAndChangeItInPlace()
    {
        var pilot = InventoryRows.Product("pilot");
        await using (var ctx = db.NewContext())
            await new ProductsRepository(ctx).CreateAsync(pilot);

        await using (var ctx = db.NewContext())
        {
            var statuses = new ServiceStatusesRepository(ctx);
            await statuses.SetAsync(pilot.Id, "web", ProductStatus.Inactive);
            await statuses.SetAsync(pilot.Id, "api", ProductStatus.Inactive);
        }
        await using (var ctx = db.NewContext())
            await new ServiceStatusesRepository(ctx).SetAsync(pilot.Id, "web", ProductStatus.Active);

        await using var read = db.NewContext();
        (await new ServiceStatusesRepository(read).GetAllAsync()).Select(row => (row.Service, row.Status))
            .Should().Equal(("api", ProductStatus.Inactive), ("web", ProductStatus.Active));
    }

    [Fact]
    public async Task Rows_ShouldGoWithTheirProduct()
    {
        var pilot = InventoryRows.Product("pilot");
        await using (var ctx = db.NewContext())
        {
            await new ProductsRepository(ctx).CreateAsync(pilot);
            await new ServiceStatusesRepository(ctx).SetAsync(pilot.Id, "api", ProductStatus.Inactive);
        }

        await using (var ctx = db.NewContext())
        {
            ctx.Products.Remove(await ctx.Products.SingleAsync());
            await ctx.SaveChangesAsync();
        }

        await using var read = db.NewContext();
        (await read.ServiceStatuses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Row_ShouldBeRejected_WhenItsProductDoesNotExist()
    {
        await using var ctx = db.NewContext();
        ctx.ServiceStatuses.Add(new ServiceStatusEntity { ProductId = Guid.NewGuid(), Service = "api", Status = ProductStatus.Inactive });

        await ctx.Invoking(context => context.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }
}
