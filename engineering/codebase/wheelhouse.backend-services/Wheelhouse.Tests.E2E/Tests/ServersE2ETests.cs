using System.Net;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Backend.Beta.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>Verifies the server inventory boundary: the operator reads and edits servers; nobody else does.</summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ServersE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task Get_ShouldReturn401_WhenTheCallerIsAnonymous()
    {
        var response = await AnonymousClient.GetAsync("api/servers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_ShouldReturn200WithEveryServerBySlug_WhenTheOperatorReads()
    {
        var response = await AdminClient.GetAsync("api/servers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var servers = await response.ReadEnvelopeAsync<IReadOnlyList<ServerResponse>>();
        servers.Select(server => (server.Slug, server.Provider, server.Host))
            .Should().Equal(("pilot-host", "hetzner", "vps.example.net"), ("prod-host", "hetzner", "prod.example.net"));
    }

    [Fact]
    public async Task Post_ShouldReturn400_WhenTheActionHeaderIsMissing()
    {
        var response = await AdminClient.PostJsonAsync("api/servers",
            new { slug = "hel2", name = "Helsinki 2", provider = "hetzner", host = "hel2.example.net", region = "hel1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
