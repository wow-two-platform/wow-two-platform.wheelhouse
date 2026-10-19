using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the other-console address the environment tag switches to: <c>Console:PeerUrl</c> is a host fact, so only the
/// owner's session reads it (never the anonymous status, an integration key or a stranger), it reads empty until the
/// setting names a peer, it names the environment the peer serves, and a malformed value fails the boot. Each case boots
/// its own host over the run's database, since the setting binds once per host.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ConsolePeerE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string PeerUrl = "https://wheelhouse-peer.example.test:8443";

    /// <summary>A host with the shared fixture's wiring and the given settings added; none leaves the setting unset.</summary>
    private WebApplicationFactory<Program> HostWith(params (string Key, string? Value)[] settings) =>
        Fixture.Host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => setting.Value))));

    private static HttpClient AdminOf(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthExtensions.AdminHeader, "1");
        return client;
    }

    private static async Task<(string? Url, string? Environment)> ReadPeerAsync(HttpClient client)
    {
        var response = await client.GetAsync("api/system/peer");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        string? Read(string name) => data.GetProperty(name).ValueKind == JsonValueKind.Null ? null : data.GetProperty(name).GetString();
        return (Read("url"), Read("environment"));
    }

    [Theory]
    [InlineData("dev", "prod")]
    [InlineData("prod", "dev")]
    public async Task Peer_ShouldNameTheOtherConsoleAndItsEnvironment_WhenAPeerUrlIsSet(string served, string peer)
    {
        using var host = HostWith(("Console:PeerUrl", PeerUrl + "/"));
        var admin = AdminOf(host);
        // The runner's variable, set on the booted host as the status tests do: naming it at boot would read as deployed.
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        configuration["DEPLOY_ENVIRONMENT"] = served;

        var (url, environment) = await ReadPeerAsync(admin);

        url.Should().Be(PeerUrl, "the origin is normalized without a trailing slash");
        environment.Should().Be(peer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Peer_ShouldBeEmpty_WhenNoPeerUrlIsSet(string? value)
    {
        using var host = value is null ? HostWith() : HostWith(("Console:PeerUrl", value));

        var (url, environment) = await ReadPeerAsync(AdminOf(host));

        url.Should().BeNull();
        environment.Should().BeNull();
    }

    [Fact]
    public async Task Peer_ShouldStayOutOfTheAnonymousStatus_WhenAPeerUrlIsSet()
    {
        using var host = HostWith(("Console:PeerUrl", PeerUrl));

        var body = await host.CreateClient().GetStringAsync("api/system/status");

        body.Should().NotContain("example.test").And.NotContain("peer", "the status stays the liveness shape");
    }

    [Fact]
    public async Task Peer_ShouldRefuseAnonymousCallersWith401_AndIntegrationKeysWith403()
    {
        using var host = HostWith(("Console:PeerUrl", PeerUrl));
        var admin = AdminOf(host);
        admin.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-create");
        var created = await admin.PostAsJsonAsync("api/integration-keys", new { name = "Reader", scopes = new[] { "catalog:read" } });
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("secret").GetString();
        var key = host.CreateClient();
        key.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        (await host.CreateClient().GetAsync("api/system/peer")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await key.GetAsync("api/system/peer")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("http://wheelhouse-peer.example.test")]
    [InlineData("wheelhouse-peer.example.test")]
    [InlineData("https://wheelhouse-peer.example.test/p/foreverpin")]
    [InlineData("https://owner@wheelhouse-peer.example.test")]
    public void Boot_ShouldFail_WhenThePeerUrlIsNotAnHttpsOrigin(string value)
    {
        using var host = HostWith(("Console:PeerUrl", value));

        var boot = () => host.CreateClient();

        boot.Should().Throw<Exception>().Which.ToString().Should().Contain("Console:PeerUrl must be empty or");
    }
}
