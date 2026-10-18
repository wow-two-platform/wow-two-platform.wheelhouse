using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Wheelhouse.Persistence.Repositories;
using Wheelhouse.Tests.Integration.Harness;
using Wheelhouse.Tests.Integration.Support;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>
/// The workspaces store over a real relational database: the owner's order, the next place, a reorder in one save,
/// the product lookups by identifier or slug and per workspace, and the constraints that keep every product in exactly
/// one existing workspace.
/// </summary>
[Collection(WheelhouseTestDbCollection.Name)]
public sealed class WorkspaceRepositoryTests(WheelhouseTestDb db) : IAsyncLifetime
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
    public async Task Workspaces_ShouldListByOrderThenSlug_AndGiveTheNextPlace()
    {
        await using (var ctx = db.NewContext())
        {
            var workspaces = new WorkspacesRepository(ctx);
            await workspaces.CreateAsync(InventoryRows.Workspace("zeta", 2));
            await workspaces.CreateAsync(InventoryRows.Workspace("beta", 1));
            await workspaces.CreateAsync(InventoryRows.Workspace("alpha", 1));
        }

        await using var read = db.NewContext();
        var repository = new WorkspacesRepository(read);
        (await repository.GetAllAsync()).Select(workspace => workspace.Slug).Should().Equal("own-ventures", "alpha", "beta", "zeta");
        (await repository.NextOrderAsync()).Should().Be(3);
        (await repository.GetBySlugAsync("beta"))!.Order.Should().Be(1);
        (await repository.GetBySlugAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task NextOrder_ShouldBeZero_WhenNoWorkspaceExists()
    {
        await db.ResetAsync();

        await using var read = db.NewContext();
        (await new WorkspacesRepository(read).NextOrderAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reorder_ShouldGiveEachWorkspaceItsPlaceInTheList()
    {
        var first = InventoryRows.Workspace("first", 1);
        var second = InventoryRows.Workspace("second", 2);
        await using (var ctx = db.NewContext())
        {
            var workspaces = new WorkspacesRepository(ctx);
            await workspaces.CreateAsync(first);
            await workspaces.CreateAsync(second);
        }

        await using (var ctx = db.NewContext())
            await new WorkspacesRepository(ctx).ReorderAsync([second.Id, InventoryRows.WorkspaceId, first.Id]);

        await using var read = db.NewContext();
        (await new WorkspacesRepository(read).GetAllAsync()).Select(workspace => (workspace.Slug, workspace.Order))
            .Should().Equal(("second", 0), ("own-ventures", 1), ("first", 2));
    }

    [Fact]
    public async Task Products_ShouldBeFoundByIdOrSlug_AndCountedPerWorkspace()
    {
        var pilot = InventoryRows.Product("pilot");
        var empty = InventoryRows.Workspace("empty", 1);
        await using (var ctx = db.NewContext())
        {
            await new WorkspacesRepository(ctx).CreateAsync(empty);
            await new ProductsRepository(ctx).CreateAsync(pilot);
        }

        await using var read = db.NewContext();
        var products = new ProductsRepository(read);
        (await products.FindAsync(pilot.Id.ToString()))!.Slug.Should().Be("pilot");
        (await products.FindAsync("pilot"))!.Id.Should().Be(pilot.Id);
        (await products.FindAsync(Guid.NewGuid().ToString())).Should().BeNull();
        (await products.FindAsync("ghost")).Should().BeNull();
        (await products.AnyInWorkspaceAsync(InventoryRows.WorkspaceId), await products.AnyInWorkspaceAsync(empty.Id))
            .Should().Be((true, false));
    }

    [Fact]
    public async Task WorkspaceSlug_ShouldBeRejected_WhenAnotherWorkspaceHasIt()
    {
        await using var ctx = db.NewContext();
        ctx.Workspaces.Add(InventoryRows.Workspace("own-ventures", 1));

        await ctx.Invoking(context => context.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Workspace_ShouldStay_WhileAProductBelongsToIt()
    {
        await using (var ctx = db.NewContext())
            await new ProductsRepository(ctx).CreateAsync(InventoryRows.Product("pilot"));

        await using var remove = db.NewContext();
        remove.Workspaces.Remove(await remove.Workspaces.SingleAsync());

        await remove.Invoking(context => context.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Product_ShouldBeRejected_WhenItsWorkspaceDoesNotExist()
    {
        await using var ctx = db.NewContext();
        ctx.Products.Add(InventoryRows.Product("stray") with { WorkspaceId = Guid.NewGuid() });

        await ctx.Invoking(context => context.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }
}
