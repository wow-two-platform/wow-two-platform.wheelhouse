using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Infrastructure.Vaults;
using Wheelhouse.Persistence;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Backend.Beta.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Backend.Beta.Http.Safety;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the host wiring the SDK owns: the SPA bundle served inside the secure-header pipeline, the cookie key ring's
/// application name (sign-in sessions depend on it), the readiness probe, the migration version stamp, the guarded
/// vault client and the lazily bound deployment settings.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class HostWiringE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task GetRoot_ShouldCarryTheSecureHeaders_WhenTheBundleServesTheShell()
    {
        var response = await AnonymousClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
    }

    [Fact]
    public void DataProtection_ShouldKeepTheWheelhouseApplicationName()
    {
        var options = Fixture.Host.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        options.ApplicationDiscriminator.Should().Be("Wheelhouse");
    }

    [Fact]
    public async Task Ready_ShouldReturn200WithoutABody_WhenTheDatabaseAcceptsConnections()
    {
        var response = await AnonymousClient.GetAsync("/api/system/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Migrations_ShouldCarryTheProductVersion_WhenApplied()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<WheelhouseDbContext>().Database;

        var stamps = await database.SqlQuery<string>($"SELECT DISTINCT version AS \"Value\" FROM migration_history").ToListAsync();

        stamps.Should().Equal(MigrationVersionMapper.ToVersion(typeof(Program).Assembly));
    }

    [Fact]
    public async Task VaultClient_ShouldReachPrivateHttpAddresses_ButRefuseCredentialsInTheAddress()
    {
        using var client = Fixture.Host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(VaultGateway.ClientName);

        // A closed loopback port: the guard lets the private plain-HTTP address through, so the socket refuses it.
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://127.0.0.1:9/"));
        await Assert.ThrowsAsync<OutboundAddressBlockedException>(() => client.GetAsync("http://admin:secret@127.0.0.1:9/"));
    }

    [Fact]
    public void DeploymentSettings_ShouldBindTheHostConfiguration_WhenResolved()
    {
        var settings = Fixture.Host.Services.GetRequiredService<DeploymentSettings>();

        settings.Root.Should().Be(Fixture.InventoryRoot);
    }
}
