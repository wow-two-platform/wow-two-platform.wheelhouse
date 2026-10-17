using AwesomeAssertions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;
using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Tests.Integration.Harness;
using Wheelhouse.Tests.Integration.Support;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>
/// The EF ↔ schema enum contract: the SDK converter (wired model-wide by <c>ApplyEnumStringConversions</c>) stores each enum
/// as snake_case <c>text</c>, and EF reads it back to the right member — including the multi-word
/// <c>RolledBack ↔ rolled_back</c> case single-word casing would miss. Asserts the on-disk text over the context's own
/// connection, provider-agnostic.
/// </summary>
[Collection(WheelhouseTestDbCollection.Name)]
public sealed class EnumRoundTripTests(WheelhouseTestDb db) : IAsyncLifetime
{
    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        await InventoryRows.SeedWorkspaceAsync(db);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(VpsProvider.Hetzner, "hetzner")]
    [InlineData(VpsProvider.Local, "local")]
    [InlineData(VpsProvider.Ovhcloud, "ovhcloud")]
    public async Task InventoryEnums_ShouldBeStoredAsSnakeCaseTextAndReadBack_WhenRecorded(VpsProvider provider, string storedProvider)
    {
        var product = InventoryRows.Product("pilot") with { Status = ProductStatus.Inactive };
        var server = InventoryRows.Server("hel1") with { Provider = provider };
        await using (var ctx = db.NewContext())
        {
            ctx.Products.Add(product);
            ctx.Servers.Add(server);
            ctx.Targets.Add(InventoryRows.Target("pilot-prod", product, server));
            ctx.ServiceStatuses.Add(new ServiceStatusEntity { ProductId = product.Id, Service = "api", Status = ProductStatus.Inactive });
            await ctx.SaveChangesAsync();
        }

        (await ReadAsync("select status from products where slug = 'pilot'")).Should().Be("inactive");
        (await ReadAsync("select status from service_statuses where service = 'api'")).Should().Be("inactive");
        (await ReadAsync("select provider from servers where slug = 'hel1'")).Should().Be(storedProvider);
        (await ReadAsync("select environment from targets where slug = 'pilot-prod'")).Should().Be("prod");

        await using var read = db.NewContext();
        (await read.Products.SingleAsync()).Status.Should().Be(ProductStatus.Inactive);
        (await read.ServiceStatuses.SingleAsync()).Status.Should().Be(ProductStatus.Inactive);
        (await read.Servers.SingleAsync()).Provider.Should().Be(provider);
        (await read.Targets.SingleAsync()).Environment.Should().Be(DeploymentEnvironment.Prod);
    }

    [Fact]
    public async Task MultiWordEnum_ShouldBeStoredAsSnakeCase_WhenRecorded()
    {
        var id = Guid.NewGuid();
        await using (var ctx = db.NewContext())
        {
            ctx.Deployments.Add(new Deployment
            {
                Id = id,
                ProductId = Guid.NewGuid(),
                ServerId = Guid.NewGuid(),
                Status = DeploymentStatus.RolledBack,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        (await ReadAsync("select status from deployments where id = @id", new { id })).Should().Be("rolled_back");
        await using var read = db.NewContext();
        (await read.Deployments.FindAsync(id))!.Status.Should().Be(DeploymentStatus.RolledBack);
    }

    /// <summary>Reads one scalar over the context's own connection, bypassing EF.</summary>
    private async Task<string?> ReadAsync(string sql, object? parameters = null)
    {
        await using var ctx = db.NewContext();
        return await ctx.Database.GetDbConnection().ExecuteScalarAsync<string>(sql, parameters);
    }
}
