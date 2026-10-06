using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Backend.Beta.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>Verifies MCP transport, real integration-key scopes and the read-only tool boundary.</summary>
[Collection(WheelhouseCollection.Name)]
public sealed class McpE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    [Fact]
    public async Task Endpoint_RefusesAnonymousAndCookieOnlyClients()
    {
        (await SendAsync(AnonymousClient, "tools/list")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(AdminClient, "tools/list")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CatalogKey_OnlyDiscoversProjectsAndCannotCallFleetTools()
    {
        var key = await CreateKeyAsync("catalog:read");
        using var client = KeyClient(key.Secret);
        using var list = await ReadAsync(await SendAsync(client, "tools/list"));
        Names(list).Should().Equal("list_projects");

        using var projects = await CallAsync(client, "list_projects");
        projects.RootElement.GetProperty("result").ToString().Should().Contain("foreverpin").And.NotContain("secrets");

        var previousRead = Fixture.Deployments.LastRead;
        using var denied = await CallAsync(client, "list_deployments");
        denied.RootElement.TryGetProperty("error", out _).Should().BeTrue();
        Fixture.Deployments.LastRead.Should().Be(previousRead);
    }

    [Fact]
    public async Task FleetKey_OnlyDiscoversReadOnlyFleetTools()
    {
        var key = await CreateKeyAsync("deployments:read");
        using var client = KeyClient(key.Secret);
        using var list = await ReadAsync(await SendAsync(client, "tools/list"));
        Names(list).Should().BeEquivalentTo("list_servers", "list_targets", "list_deployments",
            "get_deployment_state", "check_target", "get_vitals", "get_health");

        foreach (var tool in list.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray())
            tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();

        using var servers = await CallAsync(client, "list_servers");
        servers.RootElement.GetProperty("result").ToString().Should().Contain("pilot-host");
        using var health = await CallAsync(client, "get_health");
        health.RootElement.GetProperty("result").ToString().Should().Contain("databaseReady");
        using var projects = await CallAsync(client, "list_projects");
        projects.RootElement.TryGetProperty("error", out _).Should().BeTrue();

        (await client.GetAsync("api/integration-keys")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("api/deployments/targets")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tools_InvokeExistingReadsAndRejectInvalidTargetNames()
    {
        var key = await CreateKeyAsync("deployments:read");
        using var client = KeyClient(key.Secret);
        using var history = await CallAsync(client, "list_deployments");
        history.RootElement.GetProperty("result").ToString().Should().Contain("pilot");
        Fixture.Deployments.LastRead.Should().Be(("jobs", (string?)null));

        using var state = await CallAsync(client, "get_deployment_state", new { target = "pilot" });
        state.RootElement.GetProperty("result").ToString().Should().Contain("needs_reconciliation");
        Fixture.Deployments.LastRead.Should().Be(("state", "pilot"));

        using var invalid = await CallAsync(client, "get_deployment_state", new { target = "../../etc" });
        invalid.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
        Fixture.Deployments.LastRead.Should().Be(("state", "pilot"));

        using var newline = await CallAsync(client, "get_deployment_state", new { target = "pilot\n" });
        newline.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
        Fixture.Deployments.LastRead.Should().Be(("state", "pilot"));

        using var check = await CallAsync(client, "check_target", new { target = "pilot" });
        check.RootElement.GetProperty("result").ToString().Should().Contain("checks");
        Fixture.Deployments.LastTarget.Should().Be("pilot");
        Fixture.Deployments.LastRelease.Should().BeNull();
    }

    [Fact]
    public async Task Revocation_StopsMcpAccessOnTheFollowingRequest()
    {
        var key = await CreateKeyAsync("catalog:read", "deployments:read");
        using var client = KeyClient(key.Secret);
        using var initialization = await SendAsync(client, "initialize", new
        {
            protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "wheelhouse-test", version = "1" }
        });
        initialization.StatusCode.Should().Be(HttpStatusCode.OK);
        initialization.Headers.Contains("Mcp-Session-Id").Should().BeFalse();

        using var admin = AdminClient;
        admin.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-revoke");
        (await admin.PostAsync($"api/integration-keys/{key.Key.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "tools/list")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<IntegrationKeyWithSecretResponse> CreateKeyAsync(params string[] scopes)
    {
        using var admin = AdminClient;
        admin.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "key-create");
        var response = await admin.PostJsonAsync("api/integration-keys", new { name = "MCP test", scopes });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.ReadEnvelopeAsync<IntegrationKeyWithSecretResponse>();
    }

    private HttpClient KeyClient(string secret)
    {
        var client = AnonymousClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private static IEnumerable<string?> Names(JsonDocument list) => list.RootElement.GetProperty("result")
        .GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString());

    private static async Task<JsonDocument> CallAsync(HttpClient client, string name, object? arguments = null) =>
        await ReadAsync(await SendAsync(client, "tools/call", new { name, arguments = arguments ?? new { } }));

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, object? parameters = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } })
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        return client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var json = body.Split('\n').LastOrDefault(line => line.StartsWith("data: ", StringComparison.Ordinal));
        return JsonDocument.Parse(json is null ? body : json[6..]);
    }
}
