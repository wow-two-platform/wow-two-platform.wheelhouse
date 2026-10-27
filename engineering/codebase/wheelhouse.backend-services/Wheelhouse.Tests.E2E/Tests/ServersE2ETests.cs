using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>Verifies the server inventory boundary: the owner reads and edits servers; nobody else does.</summary>
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
    public async Task Get_ShouldReturn200WithEveryServerBySlug_WhenTheOwnerReads()
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

    [Fact]
    public async Task ConsoleUrl_ShouldBeStoredReadAndCleared_WhenTheOwnerSavesAServer()
    {
        var created = await ActionClient("server").PostJsonAsync("api/servers",
            new { slug = "bhs1", name = "Beauharnois", provider = "ovhcloud", host = "bhs1.example.net", region = "bhs",
                consoleUrl = " https://ca.ovh.com/manager/ " });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await created.ReadEnvelopeAsync<ServerResponse>()).ConsoleUrl.Should().Be("https://ca.ovh.com/manager/");
        (await ReadServerAsync("bhs1")).ConsoleUrl.Should().Be("https://ca.ovh.com/manager/");
        (await ReadServerAsync("pilot-host")).ConsoleUrl.Should().BeNull("a server without one keeps its provider's default");
        var exported = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Fixture.InventoryRoot, "inventory.json")))
            .RootElement.GetProperty("servers").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "bhs1");
        exported.EnumerateObject().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("console", StringComparison.OrdinalIgnoreCase), "the runner never reads it");

        var cleared = await ActionClient("server").PutJsonAsync("api/servers/bhs1",
            new { name = "Beauharnois", provider = "ovhcloud", host = "bhs1.example.net", region = "bhs", consoleUrl = "" });

        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadServerAsync("bhs1")).ConsoleUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("http://www.ovh.com/manager/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("www.ovh.com/manager/")]
    [InlineData("https://owner:secret@www.ovh.com/manager/")]
    [InlineData("ftp://www.ovh.com/")]
    public async Task ConsoleUrl_ShouldBeRefusedNamingTheField_WhenItIsNotAnHttpsAddress(string consoleUrl)
    {
        var created = await ActionClient("server").PostJsonAsync("api/servers", NewServer with { ConsoleUrl = consoleUrl });
        var updated = await ActionClient("server").PutJsonAsync("api/servers/pilot-host",
            new { name = "Pilot host", provider = "hetzner", host = "vps.example.net", region = "pilot", consoleUrl });

        foreach (var response in new[] { created, updated })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var errors = (JsonElement)(await response.ReadProblemAsync()).Extensions["errors"]!;
            errors.EnumerateArray().Select(error => error.GetProperty("property").GetString())
                .Should().ContainSingle().Which.Should().BeEquivalentTo("ConsoleUrl");
        }
        (await ReadServersAsync()).Should().NotContain(server => server.Slug == "hel2")
            .And.ContainSingle(server => server.Slug == "pilot-host").Which.ConsoleUrl.Should().BeNull();
    }

    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private async Task<IReadOnlyList<ServerResponse>> ReadServersAsync() =>
        await (await AdminClient.GetAsync("api/servers")).ReadEnvelopeAsync<IReadOnlyList<ServerResponse>>();

    private async Task<ServerResponse> ReadServerAsync(string slug) =>
        (await ReadServersAsync()).Single(server => server.Slug == slug);

    private sealed record NewServerBody(string Slug, string Name, string Provider, string Host, string Region, string? ConsoleUrl = null);

    private static NewServerBody NewServer => new("hel2", "Helsinki 2", "hetzner", "hel2.example.net", "hel1");
}
