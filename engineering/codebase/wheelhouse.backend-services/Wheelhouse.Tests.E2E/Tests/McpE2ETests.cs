using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

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
        projects.RootElement.GetProperty("result").ToString().Should().Contain("foreverpin");

        var previousRead = Fixture.Deployments.LastRead;
        using var denied = await CallAsync(client, "list_deployments");
        denied.RootElement.TryGetProperty("error", out _).Should().BeTrue();
        Fixture.Deployments.LastRead.Should().Be(previousRead);
    }

    [Fact]
    public async Task CatalogKey_ReadsTheSameProjectionOverRestAndMcp()
    {
        var key = await CreateKeyAsync("catalog:read");
        using var client = KeyClient(key.Secret);

        var rest = await client.GetAsync("api/products");
        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        using var restBody = JsonDocument.Parse(await rest.Content.ReadAsStringAsync());
        using var call = await CallAsync(client, "list_projects");
        using var mcpBody = JsonDocument.Parse(call.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);

        JsonElement.DeepEquals(restBody.RootElement.GetProperty("data"), mcpBody.RootElement).Should().BeTrue();
        var dev = mcpBody.RootElement.EnumerateArray().Single(p => p.GetProperty("slug").GetString() == "foreverpin")
            .GetProperty("environments").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "dev");
        dev.GetProperty("secrets").GetProperty("vault").GetString().Should().Be("pilot-vault");
        dev.GetProperty("secrets").GetProperty("namespace").GetString().Should().Be("foreverpin-dev");
    }

    [Fact]
    public async Task CatalogKey_ListsTheManifestIdentity_WhenARepositoryCarriesOne()
    {
        Fixture.ProductManifests.Push(
            "sulton-max/10x-venture-forever-pin",
            "{ \"manifest\": 1, \"name\": \"Forever Pin\", \"description\": \"From the repository.\", \"homepage\": \"https://foreverpin.example\" }");
        var key = await CreateKeyAsync("catalog:read");
        using var client = KeyClient(key.Secret);

        using var projects = await CallAsync(client, "list_projects");

        projects.RootElement.GetProperty("result").ToString()
            .Should().Contain("Forever Pin").And.Contain("From the repository.").And.Contain("https://foreverpin.example");
    }

    [Fact]
    public async Task ListProjects_KeepsCamelCaseNamesStringEnumsAndNullFields()
    {
        var key = await CreateKeyAsync("catalog:read");
        using var client = KeyClient(key.Secret);

        using var projects = await CallAsync(client, "list_projects");

        var text = projects.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        using var payload = JsonDocument.Parse(text!);
        var project = payload.RootElement.EnumerateArray().Single(p => p.GetProperty("slug").GetString() == "foreverpin");
        project.GetProperty("homepage").ValueKind.Should().Be(JsonValueKind.Null);
        project.GetProperty("lifecycle").ValueKind.Should().Be(JsonValueKind.String);
        project.TryGetProperty("Slug", out _).Should().BeFalse();
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
    public async Task ToolFailures_CarryTheOperatorSafeErrorAndARetryHint()
    {
        var key = await CreateKeyAsync("deployments:read");
        using var client = KeyClient(key.Secret);

        const string timeout = "Deployment response timed out. Reconcile target state before retrying.";
        Fixture.Deployments.ReadFailure = AppErrorFactory.OperationTimeout(timeout);
        (await ToolErrorAsync(client, "list_deployments")).Should().Be(("OperationTimeout", timeout, true));

        const string unreachable = "Deployment step failed: SSH connection refused (exit 255); inspect the target privately";
        Fixture.Deployments.ReadFailure = AppErrorFactory.ExternalUnavailable(unreachable);
        (await ToolErrorAsync(client, "get_vitals")).Should().Be(("ExternalUnavailable", unreachable, true));

        const string refused = "Deployment rejected: Unknown target";
        Fixture.Deployments.ReadFailure = AppErrorFactory.Conflict(refused);
        (await ToolErrorAsync(client, "get_deployment_state", new { target = "pilot" }))
            .Should().Be(("Conflict", refused, false));
    }

    [Fact]
    public async Task InvalidTargetName_IsANonRetryableValidationError()
    {
        var key = await CreateKeyAsync("deployments:read");
        using var client = KeyClient(key.Secret);
        var previousTarget = Fixture.Deployments.LastTarget;

        (await ToolErrorAsync(client, "check_target", new { target = "../../etc" })).Should().Be(
            ("Validation", "Target must be an existing lowercase target slug, at most 48 characters.", false));
        Fixture.Deployments.LastTarget.Should().Be(previousTarget);
    }

    [Fact]
    public async Task UnexpectedToolException_ReachesTheAgentAsAGenericError()
    {
        var key = await CreateKeyAsync("deployments:read");
        using var client = KeyClient(key.Secret);
        Fixture.Deployments.ReadException = new InvalidOperationException(
            "ssh deploy@10.0.0.5 -i /srv/wheelhouse/ssh/pilot-host/id_ed25519 failed\n   at Runner.Run()");

        using var call = await CallAsync(client, "list_deployments");

        var text = ErrorText(call);
        text.Should().NotContain("10.0.0.5").And.NotContain("/srv/").And.NotContain("Runner.Run");
        Parse(text).Should().Be(("Unexpected", "An unexpected error occurred.", false));
    }

    [Fact]
    public async Task ForeignOrigin_IsRefusedBeforeAuthentication()
    {
        var key = await CreateKeyAsync("catalog:read");
        using var client = KeyClient(key.Secret);

        (await SendAsync(client, "tools/list", origin: "https://rebound.example")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(client, "tools/list", origin: "http://localhost:8443")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(client, "tools/list", origin: "null")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(AnonymousClient, "tools/list", origin: "https://rebound.example")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConsoleAndConfiguredOrigins_AndOriginlessClients_ReachTheTools()
    {
        var key = await CreateKeyAsync("catalog:read");
        using var client = KeyClient(key.Secret);

        // The test host is reached at http://localhost, so that is the console's own origin here.
        foreach (var origin in new[] { "http://localhost", WheelhouseAppFixture.McpAllowedOrigin, null })
        {
            using var list = await ReadAsync(await SendAsync(client, "tools/list", origin: origin));
            Names(list).Should().Equal("list_projects");
        }
    }

    [Fact]
    public async Task OversizedBody_Is413_WhetherDeclaredOrStreamed()
    {
        var key = await CreateKeyAsync("deployments:read");
        using var client = KeyClient(key.Secret);

        var declared = new ByteArrayContent(new byte[64 * 1024 + 1]);
        declared.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        (await PostAsync(client, declared)).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        // JsonContent streams without a Content-Length, so only the bytes read can bound it.
        (await PostAsync(client, ToolCall(new string('a', 70_000)))).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        // A body within the limit still reaches the tool, which refuses the name itself.
        using var within = await ReadAsync(await PostAsync(client, ToolCall(new string('a', 60_000))));
        within.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
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

    private static async Task<(string? Code, string? Message, bool Retryable)> ToolErrorAsync(
        HttpClient client, string name, object? arguments = null)
    {
        using var call = await CallAsync(client, name, arguments);
        return Parse(ErrorText(call));
    }

    private static string ErrorText(JsonDocument call)
    {
        var result = call.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    private static (string? Code, string? Message, bool Retryable) Parse(string text)
    {
        using var error = JsonDocument.Parse(text);
        var root = error.RootElement;
        return (root.GetProperty("code").GetString(), root.GetProperty("message").GetString(), root.GetProperty("retryable").GetBoolean());
    }

    private static async Task<JsonDocument> CallAsync(HttpClient client, string name, object? arguments = null) =>
        await ReadAsync(await SendAsync(client, "tools/call", new { name, arguments = arguments ?? new { } }));

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, string method, object? parameters = null, string? origin = null) =>
        PostAsync(client, JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } }), origin);

    private static JsonContent ToolCall(string target) => JsonContent.Create(new
    {
        jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "check_target", arguments = new { target } }
    });

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, HttpContent content, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = content };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        if (origin is not null)
            request.Headers.Add("Origin", origin);
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
