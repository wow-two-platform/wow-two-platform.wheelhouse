using System.Net;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Backend.Beta.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the auth gate across the control-plane surface — every anonymous request to a Products, Integration keys or
/// Servers endpoint is refused, reads and mutations alike, while the same call from the operator gets through.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class AuthGateE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private static object ServerBody => new
    {
        slug = "auth-srv", name = "Auth server", provider = "hetzner", host = "10.9.9.9", sshUser = "root", sshPort = 22, region = "hel1",
    };

    [Theory]
    [InlineData("api/products")]
    [InlineData("api/products/foreverpin")]
    [InlineData("api/integration-keys")]
    public async Task Get_ShouldReturn401_WhenTheCallerIsAnonymous(string path)
    {
        var response = await AnonymousClient.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateLifecycle_ShouldReturn401_WhenTheCallerIsAnonymous()
    {
        var response = await AnonymousClient.PutJsonAsync("api/products/foreverpin/lifecycle", new { lifecycle = "live" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateKey_ShouldReturn401_WhenTheCallerIsAnonymous()
    {
        var response = await AnonymousClient.PostJsonAsync("api/integration-keys", new { name = "probe", scopes = new[] { "catalog:read" } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostServers_ShouldReturn401_WhenTheCallerIsAnonymous()
    {
        var response = await AnonymousClient.PostJsonAsync("api/servers", ServerBody);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MutatingEndpoints_ShouldBeReachable_WhenTheOperatorCalls()
    {
        // The same calls that 401 anonymously must pass for the operator — proves the gate, not a blanket block.
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "lifecycle");
        (await client.PutJsonAsync("api/products/foreverpin/lifecycle", new { lifecycle = "paused" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var servers = AdminClient;
        servers.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "server");
        (await servers.PostJsonAsync("api/servers", ServerBody)).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
