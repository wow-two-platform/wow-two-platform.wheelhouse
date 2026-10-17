using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Wheelhouse.Domain.Pins.Entities;
using Wheelhouse.Domain.Pins.Enums;
using Wheelhouse.Persistence.Repositories;
using Wheelhouse.Tests.Integration.Harness;
using Wheelhouse.Tests.Integration.Support;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>
/// The pins store over a real relational database: one owner's pins in their order, the next place per owner, a target
/// found with its null parts, a reorder in one save, and the constraints — a pin names exactly its kind's target, an
/// owner pins a target once (Postgres compares the null parts as equal), and a pin goes with its workspace or product.
/// </summary>
[Collection(WheelhouseTestDbCollection.Name)]
public sealed class PinRepositoryTests(WheelhouseTestDb db) : IAsyncLifetime
{
    private static PinEntity Pin(string owner, PinKind kind, int order, Guid? workspace = null, Guid? product = null, string? service = null) => new()
    {
        Id = Guid.NewGuid(), Owner = owner, Kind = kind, WorkspaceId = workspace, ProductId = product, Service = service, Order = order,
    };

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        await InventoryRows.SeedWorkspaceAsync(db);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Pins_ShouldListOneOwnersInOrder_AndGiveTheNextPlacePerOwner()
    {
        var pilot = InventoryRows.Product("pilot");
        await using (var ctx = db.NewContext())
        {
            await new ProductsRepository(ctx).CreateAsync(pilot);
            var pins = new PinsRepository(ctx);
            await pins.CreateAsync(Pin("max", PinKind.Product, 2, product: pilot.Id));
            await pins.CreateAsync(Pin("max", PinKind.Workspace, 0, workspace: InventoryRows.WorkspaceId));
            await pins.CreateAsync(Pin("max", PinKind.Service, 1, product: pilot.Id, service: "api"));
            await pins.CreateAsync(Pin("other", PinKind.Product, 7, product: pilot.Id));
        }

        await using var read = db.NewContext();
        var repository = new PinsRepository(read);
        (await repository.GetForOwnerAsync("max")).Select(pin => pin.Kind)
            .Should().Equal(PinKind.Workspace, PinKind.Service, PinKind.Product);
        (await repository.NextOrderAsync("max"), await repository.NextOrderAsync("other"), await repository.NextOrderAsync("nobody"))
            .Should().Be((3, 8, 0));
        (await repository.FindAsync("max", PinKind.Product, null, pilot.Id, null))!.Order.Should().Be(2);
        (await repository.FindAsync("max", PinKind.Service, null, pilot.Id, "api")).Should().NotBeNull();
        (await repository.FindAsync("max", PinKind.Service, null, pilot.Id, "web")).Should().BeNull();
        (await repository.FindAsync("other", PinKind.Workspace, InventoryRows.WorkspaceId, null, null)).Should().BeNull();
    }

    [Fact]
    public async Task Reorder_ShouldGiveEachOfTheOwnersPinsItsPlace_AndLeaveOtherOwnersAlone()
    {
        var pilot = InventoryRows.Product("pilot");
        var first = Pin("max", PinKind.Product, 0, product: pilot.Id);
        var second = Pin("max", PinKind.Workspace, 1, workspace: InventoryRows.WorkspaceId);
        var theirs = Pin("other", PinKind.Product, 0, product: pilot.Id);
        await using (var ctx = db.NewContext())
        {
            await new ProductsRepository(ctx).CreateAsync(pilot);
            var pins = new PinsRepository(ctx);
            foreach (var pin in new[] { first, second, theirs })
                await pins.CreateAsync(pin);
        }

        await using (var ctx = db.NewContext())
            await new PinsRepository(ctx).ReorderAsync("max", [second.Id, theirs.Id, first.Id]);

        await using var read = db.NewContext();
        (await read.Pins.AsNoTracking().OrderBy(pin => pin.Owner).ThenBy(pin => pin.Order).Select(pin => pin.Id).ToListAsync())
            .Should().Equal(second.Id, first.Id, theirs.Id);
        (await read.Pins.SingleAsync(pin => pin.Id == first.Id)).Order.Should().Be(2);
        (await read.Pins.SingleAsync(pin => pin.Id == theirs.Id)).Order.Should().Be(0);
    }

    [Fact]
    public async Task Pin_ShouldBeRejected_WhenItNamesAnotherTargetThanItsKindTakes()
    {
        var pilot = InventoryRows.Product("pilot");
        await using (var ctx = db.NewContext())
            await new ProductsRepository(ctx).CreateAsync(pilot);

        foreach (var pin in new[]
        {
            Pin("max", PinKind.Workspace, 0),
            Pin("max", PinKind.Workspace, 0, workspace: InventoryRows.WorkspaceId, product: pilot.Id),
            Pin("max", PinKind.Product, 0, product: pilot.Id, service: "api"),
            Pin("max", PinKind.Service, 0, product: pilot.Id),
        })
        {
            await using var ctx = db.NewContext();
            ctx.Pins.Add(pin);
            await ctx.Invoking(context => context.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
    }

    [Fact]
    public async Task Pin_ShouldBeRejected_WhenTheOwnerPinnedTheTargetAlready()
    {
        // SQLite treats every NULL in a unique index as distinct; the NULLS NOT DISTINCT target index is Postgres-only.
        if (db.Provider != DatabaseProvider.Postgres)
            return;

        await using (var ctx = db.NewContext())
        {
            ctx.Pins.Add(Pin("max", PinKind.Workspace, 0, workspace: InventoryRows.WorkspaceId));
            await ctx.SaveChangesAsync();
        }

        await using var again = db.NewContext();
        again.Pins.Add(Pin("max", PinKind.Workspace, 1, workspace: InventoryRows.WorkspaceId));
        await again.Invoking(context => context.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Pins_ShouldGoWithTheirWorkspaceOrProduct()
    {
        var pilot = InventoryRows.Product("pilot");
        var spare = InventoryRows.Workspace("spare", 1);
        await using (var ctx = db.NewContext())
        {
            await new WorkspacesRepository(ctx).CreateAsync(spare);
            await new ProductsRepository(ctx).CreateAsync(pilot);
            var pins = new PinsRepository(ctx);
            await pins.CreateAsync(Pin("max", PinKind.Workspace, 0, workspace: spare.Id));
            await pins.CreateAsync(Pin("max", PinKind.Product, 1, product: pilot.Id));
            await pins.CreateAsync(Pin("max", PinKind.Service, 2, product: pilot.Id, service: "api"));
            await pins.CreateAsync(Pin("max", PinKind.Workspace, 3, workspace: InventoryRows.WorkspaceId));
        }

        await using (var ctx = db.NewContext())
        {
            ctx.Products.Remove(await ctx.Products.SingleAsync());
            ctx.Workspaces.Remove(await ctx.Workspaces.SingleAsync(workspace => workspace.Slug == "spare"));
            await ctx.SaveChangesAsync();
        }

        await using var read = db.NewContext();
        (await read.Pins.Select(pin => pin.Order).ToListAsync()).Should().Equal(3);
    }
}
