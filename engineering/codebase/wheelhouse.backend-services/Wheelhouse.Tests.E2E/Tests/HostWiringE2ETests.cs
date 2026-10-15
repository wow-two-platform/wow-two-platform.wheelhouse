using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the host wiring the SDK owns: the SPA bundle served inside the secure-header pipeline and the cookie key ring's
/// application name, which sign-in sessions depend on.
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
}
