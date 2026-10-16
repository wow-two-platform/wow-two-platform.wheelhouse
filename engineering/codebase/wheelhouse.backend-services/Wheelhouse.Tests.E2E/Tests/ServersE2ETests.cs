using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

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
    public async Task Post_ShouldReturn400NamingTheHeader_WhenTheActionHeaderIsMissing()
    {
        var response = await AdminClient.PostJsonAsync("api/servers", NewServer);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = ((JsonElement)(await response.ReadProblemAsync()).Extensions["errors"]!)[0];
        (error.GetProperty("code").GetString(), error.GetProperty("property").GetString())
            .Should().Be(("ActionHeaderRequired", "X-Wheelhouse-Action"));
    }

    [Fact]
    public async Task Post_ShouldReturn400_WhenTheActionComesInAnotherHeader()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Requested-Action", "server");

        var response = await client.PostJsonAsync("api/servers", NewServer);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static object NewServer =>
        new { slug = "hel2", name = "Helsinki 2", provider = "hetzner", host = "hel2.example.net", region = "hel1" };
}
