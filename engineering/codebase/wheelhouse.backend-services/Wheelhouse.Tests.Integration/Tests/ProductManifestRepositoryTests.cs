using AwesomeAssertions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Models;
using Wheelhouse.Persistence.Repositories;
using Wheelhouse.Tests.Integration.Harness;
using Wheelhouse.Tests.Integration.Support;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>
/// The cached product manifests over a real relational database: one row per product, replaced in place by a newer
/// commit, the file it was read from, the per-service text as JSON, no row left tracked after a write, and the row
/// removed with its product. Runs on
/// the SDK <see cref="WheelhouseTestDb"/> (Postgres container or in-memory SQLite).
/// </summary>
[Collection(WheelhouseTestDbCollection.Name)]
public sealed class ProductManifestRepositoryTests(WheelhouseTestDb db) : IAsyncLifetime
{
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        await InventoryRows.SeedWorkspaceAsync(db);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    private static ProductManifestEntity Manifest(ProductEntity product, string commit, string name = "Pilot") => new()
    {
        ProductId = product.Id,
        Repository = product.Repository,
        Branch = product.DefaultBranch,
        Commit = commit,
        SourceFile = "engineering/deployment/deploy.yml",
        Name = name,
        Description = "From the repository.",
        Homepage = "https://pilot.example",
        Services =
        [
            new ProductServiceIdentityValueObject { Service = "api", Name = "Interface", Description = "Serves requests." },
            new ProductServiceIdentityValueObject { Service = "web", Name = "Site" },
        ],
    };

    private async Task<ProductEntity> StoreProductAsync(string slug)
    {
        var product = InventoryRows.Product(slug);
        await using var ctx = db.NewContext();
        await new ProductsRepository(ctx).CreateAsync(product);
        return product;
    }

    [Fact]
    public async Task Save_ShouldStoreTheManifestWithItsCommitAndServices_WhenTheProductHasNone()
    {
        var product = await StoreProductAsync("pilot");
        await using (var ctx = db.NewContext())
            await new EfProductManifests(ctx).SaveAsync(Manifest(product, First));

        await using var read = db.NewContext();
        var stored = (await new EfProductManifests(read).GetByProductsAsync([product.Id]))[product.Id];
        (stored.Repository, stored.Branch, stored.Commit, stored.SourceFile, stored.Name, stored.Description, stored.Homepage)
            .Should().Be(("owner/pilot", "main", First, "engineering/deployment/deploy.yml", "Pilot", "From the repository.",
                "https://pilot.example"));
        stored.Services.Should().BeEquivalentTo(Manifest(product, First).Services, options => options.WithStrictOrdering());
        var raw = await read.Database.GetDbConnection()
            .ExecuteScalarAsync<string>("select cast(services as text) from product_manifests");
        raw.Should().Contain("\"service\"").And.Contain("\"description\"");
    }

    [Fact]
    public async Task Save_ShouldReplaceTheRowInPlace_WhenANewerCommitIsSaved()
    {
        var product = await StoreProductAsync("pilot");
        await using (var ctx = db.NewContext())
            await new EfProductManifests(ctx).SaveAsync(Manifest(product, First));

        await using (var ctx = db.NewContext())
        {
            var newer = Manifest(product, Second, "Renamed");
            newer.SourceFile = "product/product.json";
            newer.Homepage = null;
            newer.Services = [];
            await new EfProductManifests(ctx).SaveAsync(newer);
        }

        await using var read = db.NewContext();
        var rows = await new EfProductManifests(read).GetByProductsAsync([product.Id]);
        var stored = rows.Should().ContainSingle().Which.Value;
        (stored.Commit, stored.SourceFile, stored.Name, stored.Homepage).Should().Be(
            (Second, "product/product.json", "Renamed", null));
        stored.Services.Should().BeEmpty();
        (await read.ProductManifests.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Save_ShouldLeaveNothingTracked_WhenTheSameContextWritesTwice()
    {
        var product = await StoreProductAsync("pilot");
        await using var ctx = db.NewContext();
        var manifests = new EfProductManifests(ctx);

        await manifests.SaveAsync(Manifest(product, First));
        await manifests.SaveAsync(Manifest(product, Second, "Second write"));
        await manifests.DeleteAsync(product.Id);
        await manifests.SaveAsync(Manifest(product, First, "After delete"));

        ctx.ChangeTracker.Entries<ProductManifestEntity>().Should().BeEmpty();
        (await manifests.GetByProductsAsync([product.Id]))[product.Id].Name.Should().Be("After delete");
    }

    [Fact]
    public async Task GetByProducts_ShouldReturnOnlyTheAskedProductsRows_WhenSeveralHaveManifests()
    {
        var alpha = await StoreProductAsync("alpha");
        var beta = await StoreProductAsync("beta");
        var gamma = await StoreProductAsync("gamma");
        await using (var ctx = db.NewContext())
        {
            var manifests = new EfProductManifests(ctx);
            await manifests.SaveAsync(Manifest(alpha, First, "Alpha"));
            await manifests.SaveAsync(Manifest(beta, Second, "Beta"));
        }

        await using var read = db.NewContext();
        var found = await new EfProductManifests(read).GetByProductsAsync([alpha.Id, gamma.Id]);

        found.Keys.Should().BeEquivalentTo([alpha.Id]);
        (await new EfProductManifests(read).GetByProductsAsync([])).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_ShouldRemoveOnlyThatProductsRow_WhenItHasOne()
    {
        var alpha = await StoreProductAsync("alpha");
        var beta = await StoreProductAsync("beta");
        await using (var ctx = db.NewContext())
        {
            var manifests = new EfProductManifests(ctx);
            await manifests.SaveAsync(Manifest(alpha, First));
            await manifests.SaveAsync(Manifest(beta, Second));
            await manifests.DeleteAsync(alpha.Id);
            await manifests.DeleteAsync(Guid.NewGuid());
        }

        await using var read = db.NewContext();
        (await new EfProductManifests(read).GetByProductsAsync([alpha.Id, beta.Id])).Keys.Should().BeEquivalentTo([beta.Id]);
    }

    [Fact]
    public async Task Save_ShouldNotThrow_WhenTheProductIsGone()
    {
        var product = InventoryRows.Product("ghost");
        await using var ctx = db.NewContext();
        var manifests = new EfProductManifests(ctx);

        await manifests.Invoking(repository => repository.SaveAsync(Manifest(product, First))).Should().NotThrowAsync();

        (await manifests.GetByProductsAsync([product.Id])).Should().BeEmpty();
        ctx.ChangeTracker.Entries<ProductManifestEntity>().Should().BeEmpty();
    }

    [Fact]
    public async Task ProductDelete_ShouldRemoveItsManifest_WhenTheProductIsDeleted()
    {
        var product = await StoreProductAsync("pilot");
        var other = await StoreProductAsync("other");
        await using (var ctx = db.NewContext())
        {
            var manifests = new EfProductManifests(ctx);
            await manifests.SaveAsync(Manifest(product, First));
            await manifests.SaveAsync(Manifest(other, Second));
        }

        await using (var ctx = db.NewContext())
        {
            var products = new ProductsRepository(ctx);
            await products.DeleteAsync((await products.GetBySlugAsync("pilot"))!);
        }

        await using var read = db.NewContext();
        (await new EfProductManifests(read).GetByProductsAsync([product.Id, other.Id])).Keys.Should().BeEquivalentTo([other.Id]);
    }
}
