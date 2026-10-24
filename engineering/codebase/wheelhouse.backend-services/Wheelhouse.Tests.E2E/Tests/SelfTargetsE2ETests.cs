using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for Wheelhouse's own environments (decision 64): Wheelhouse derives them from what this console knows about
/// itself — the control host, the <c>control</c> network, <c>/srv/wheelhouse</c>, the kit's console settings file and the
/// console site on this console's hostname (<c>Sites:console:Url</c> here) — and refuses one saved otherwise, so no client
/// types them. The control host reads as such and is never enrolled.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class SelfTargetsE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string Host = WheelhouseAppFixture.ConsoleHost;

    private HttpClient Act(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

    /// <summary>Adds the control host as the owner registers it: the host's platform address, as the consoles see it.</summary>
    private async Task AddControlHostAsync()
    {
        (await Act("server").PostJsonAsync("api/servers", new
        {
            slug = "control", name = "Control host", provider = "ovhcloud", host = "172.30.0.1", region = "control",
            sshUser = "wheelhouse-deploy", ingress = new { entryPoints = new[] { "private" }, probe = "http://127.0.0.1:18080" },
        })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>The body the environment form sends for one of Wheelhouse's own environments.</summary>
    private static JsonObject Derived(string environment, string? slug = null)
    {
        var body = new JsonObject
        {
            ["product"] = "wheelhouse", ["server"] = "control", ["environment"] = environment, ["network"] = "control",
            ["root"] = "/srv/wheelhouse",
            ["settings"] = new JsonArray(new JsonObject { ["service"] = "console", ["path"] = $"/srv/wheelhouse/config/console-{environment}.json" }),
            ["smokeChecks"] = new JsonArray(),
            ["sites"] = new JsonArray(new JsonObject { ["site"] = "console", ["host"] = Host }),
        };
        if (slug is not null)
            body["slug"] = slug;
        return body;
    }

    [Fact]
    public async Task Self_ShouldDeriveEachEnvironment_FromTheConsolesOwnHostname()
    {
        var response = await AdminClient.GetAsync("api/targets/self");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(response);
        (data.GetProperty("product").GetString(), data.GetProperty("host").GetString()).Should().Be(("wheelhouse", Host));
        data.GetProperty("environments").EnumerateArray()
            .Select(item => (item.GetProperty("environment").GetString(), item.GetProperty("network").GetString(),
                item.GetProperty("root").GetString(), item.GetProperty("settings")[0].GetProperty("path").GetString(),
                item.GetProperty("sites")[0].GetProperty("site").GetString(), item.GetProperty("sites")[0].GetProperty("host").GetString()))
            .Should().Equal(
                ("dev", "control", "/srv/wheelhouse", "/srv/wheelhouse/config/console-dev.json", "console", Host),
                ("prod", "control", "/srv/wheelhouse", "/srv/wheelhouse/config/console-prod.json", "console", Host));
        (await AnonymousClient.GetAsync("api/targets/self")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Servers_ShouldReadTheServerAtAPrivateAddressAsTheControlHost()
    {
        await AddControlHostAsync();

        var servers = (await DataAsync(await AdminClient.GetAsync("api/servers"))).EnumerateArray()
            .ToDictionary(server => server.GetProperty("slug").GetString()!, server => server.GetProperty("controlHost").GetBoolean());

        servers.Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["control"] = true, ["pilot-host"] = false, ["prod-host"] = false,
        });
    }

    [Fact]
    public async Task CreateSelfTarget_ShouldStoreWhatWheelhouseDerives_OnTheControlHost()
    {
        await AddControlHostAsync();

        foreach (var environment in new[] { "dev", "prod" })
            (await Act("target").PostJsonAsync("api/targets", Derived(environment, "wheelhouse-" + environment)))
                .StatusCode.Should().Be(HttpStatusCode.Created);

        var targets = await (await AdminClient.GetAsync("api/targets?product=wheelhouse")).ReadEnvelopeAsync<IReadOnlyList<TargetResponse>>();
        targets.Should().Equal(
            new TargetResponse("wheelhouse-dev", "wheelhouse", "control", "dev", "control", "/srv/wheelhouse"),
            new TargetResponse("wheelhouse-prod", "wheelhouse", "control", "prod", "control", "/srv/wheelhouse"));
        var exported = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture.InventoryRoot, "inventory.json"))).RootElement
            .GetProperty("targets").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "wheelhouse-dev");
        (exported.GetProperty("network").GetString(), exported.GetProperty("sites")[0].GetProperty("host").GetString())
            .Should().Be(("control", Host));

        // Editing one with the same derived definition saves.
        (await Act("target").PutJsonAsync("api/targets/wheelhouse-dev", Derived("dev"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("network", "platform", "network 'control'")]
    [InlineData("root", "/srv/console", "folder '/srv/wheelhouse'")]
    [InlineData("settings", "/srv/wheelhouse/config/console-prod.json", "settings file console → /srv/wheelhouse/config/console-dev.json")]
    [InlineData("sites", "wheelhouse-console-dev.tail1234.ts.net", "site console → " + Host)]
    [InlineData("sites", "", "site console → " + Host)]
    public async Task SaveSelfTarget_ShouldReturn400NamingWhatWheelhouseDerives_WhenAFieldDiffers(string field, string value, string expected)
    {
        await AddControlHostAsync();
        var body = Derived("dev", "wheelhouse-dev");
        body[field] = field switch
        {
            "settings" => new JsonArray(new JsonObject { ["service"] = "console", ["path"] = value }),
            "sites" when value.Length == 0 => new JsonArray(),
            "sites" => new JsonArray(new JsonObject { ["site"] = "console", ["host"] = value }),
            _ => value,
        };

        var created = await Act("target").PostJsonAsync("api/targets", body);

        created.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await created.ReadProblemAsync()).Detail.Should().StartWith("Wheelhouse derives its own environments").And.Contain(expected);
        (await (await AdminClient.GetAsync("api/targets?product=wheelhouse")).ReadEnvelopeAsync<IReadOnlyList<TargetResponse>>())
            .Should().BeEmpty();

        // An existing one cannot be changed away from what Wheelhouse derives either.
        (await Act("target").PostJsonAsync("api/targets", Derived("dev", "wheelhouse-dev"))).StatusCode.Should().Be(HttpStatusCode.Created);
        body.Remove("slug");
        (await Act("target").PutJsonAsync("api/targets/wheelhouse-dev", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SaveSelfTarget_ShouldReturn400_WhenTheServerIsNotTheControlHost()
    {
        var body = Derived("dev", "wheelhouse-dev");
        body["server"] = "pilot-host";

        var response = await Act("target").PostJsonAsync("api/targets", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Detail.Should().Contain("run on the control host").And.Contain("'pilot-host' is not it");
    }

    [Fact]
    public async Task Enroll_ShouldReturn409AndReachNoRunner_ForTheControlHost()
    {
        await AddControlHostAsync();

        var response = await Act("server").PostJsonAsync("api/servers/control/enrollment", new
        {
            publicAddress = "203.0.113.20", sshPort = 22, loginUser = "ubuntu", admin = "ubuntu",
            tailnetHostname = "control", expectedHostKey = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Contain("is the control host");
        Fixture.Infra.Calls.Should().NotContain(call => call.Action == "enroll");
    }
}
