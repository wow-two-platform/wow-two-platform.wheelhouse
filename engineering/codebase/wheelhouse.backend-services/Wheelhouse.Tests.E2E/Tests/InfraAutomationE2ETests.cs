using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.InfraJobs;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>Verifies the console's infrastructure automation: Tailscale nodes and the reviewed policy, a fresh server's
/// enrollment and a product's placement reach the runner as the owner asked, are audited, and a finished enrollment
/// moves its server onto the tailnet.</summary>
[Collection(WheelhouseCollection.Name)]
public sealed class InfraAutomationE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private StubInfraGateway Infra => Fixture.Infra;

    [Theory]
    [InlineData("api/tailnet")]
    [InlineData("api/tailnet/policy")]
    [InlineData("api/servers/prod-host/enrollment")]
    [InlineData("api/products/foreverpin/placements")]
    public async Task Reads_ShouldReturn401_WhenTheCallerIsAnonymous(string path)
    {
        (await AnonymousClient.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tailnet_ShouldPassEachServersNodeThrough_WhenTheOwnerReads()
    {
        var response = await AdminClient.GetAsync("api/tailnet");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await Data(response);
        (data.GetProperty("connected").GetBoolean(), data.GetProperty("nodes")[0].GetProperty("serverId").GetString())
            .Should().Be((true, "prod-host"));
    }

    [Fact]
    public async Task EnrollmentKey_ShouldNeedTheActionHeaderAndAKnownServer()
    {
        (await AdminClient.PostAsync("api/servers/prod-host/enrollment/key", null)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await Act("server").PostAsync("api/servers/missing/enrollment/key", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var response = await Act("server").PostAsync("api/servers/prod-host/enrollment/key", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data(response)).GetProperty("publicKey").GetString().Should().StartWith("ssh-ed25519 ");
        Infra.Calls.Should().ContainSingle(call => call.Action == "enrollment-key" && (string?)call.Request == "prod-host");
        (await LastAudit()).Should().Be(("server.enrollment.key", "prod-host", "succeeded"));
    }

    [Fact]
    public async Task Enroll_ShouldStartTheJobAsTheOwnerWithEveryFieldAndAuditIt()
    {
        var response = await Act("server").PostAsJsonAsync("api/servers/prod-host/enrollment", Enrollment());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = Infra.Calls.Should().ContainSingle(call => call.Action == "enroll").Subject;
        call.Actor.Should().Be("test-admin");
        call.Request.Should().Be(new ServerEnrollmentRequest("prod-host", "203.0.113.20", 22, "root", "ops",
            OwnerKey, "ocharo-vps", null));
        (await LastAudit()).Should().Be(("server.enroll", "prod-host", "succeeded"));
    }

    [Theory]
    [InlineData("publicAddress", "100.101.1.2")]
    [InlineData("publicAddress", "ocharo.example")]
    [InlineData("loginUser", "Root")]
    [InlineData("admin", "wheelhouse-deploy")]
    [InlineData("tailnetHostname", "Ocharo_VPS")]
    [InlineData("adminKey", "not a key")]
    [InlineData("expectedHostKey", "MD5:aa")]
    public async Task Enroll_ShouldReturn400_WhenAFieldIsMalformed(string field, string value)
    {
        var request = JsonSerializer.SerializeToNode(Enrollment())!.AsObject();
        request[field] = value;

        var response = await Act("server").PostAsJsonAsync("api/servers/prod-host/enrollment", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Infra.Calls.Should().NotContain(call => call.Action == "enroll");
    }

    [Fact]
    public async Task Enroll_ShouldRelayTheRunnersRefusal()
    {
        Infra.Refusal = "Connect Tailscale first: the enrollment mints a single-use key for the host";

        var response = await Act("server").PostAsJsonAsync("api/servers/prod-host/enrollment", Enrollment());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Contain("Connect Tailscale first");
    }

    [Fact]
    public async Task Place_ShouldStartThePlacementForAKnownProductAndServer()
    {
        (await Act("placement").PostAsJsonAsync("api/products/missing/placements", Placement())).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var response = await Act("placement").PostAsJsonAsync("api/products/foreverpin/placements", Placement());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = Infra.Calls.Should().ContainSingle(call => call.Action == "place").Subject;
        var request = (ProductPlacementRequest)call.Request!;
        (request.Product, request.Server, request.Database, call.Actor).Should().Be(("foreverpin", "prod-host", true, "test-admin"));
        request.Sites.Should().Equal(new ProductPlacementSite("app", "foreverpin-app"));
        (await LastAudit()).Should().Be(("product.place", "foreverpin", "succeeded"));
    }

    [Fact]
    public async Task Place_ShouldReturn400_WhenNothingOrAPlatformNodeIsAsked()
    {
        (await Act("placement").PostAsJsonAsync("api/products/foreverpin/placements",
            new { server = "prod-host", database = false, sites = Array.Empty<object>() })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await Act("placement").PostAsJsonAsync("api/products/foreverpin/placements",
            new { server = "prod-host", database = false, sites = new[] { new { site = "app", node = "postgres" } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Infra.Calls.Should().NotContain(call => call.Action == "place");
    }

    [Fact]
    public async Task PolicyApply_ShouldSendTheReviewedEtagAndDigestAndAuditIt()
    {
        var digest = new string('b', 64);
        (await Act("tailnet").PostAsJsonAsync("api/tailnet/policy/apply", new { etag = "\"e1\"", digest = "short" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var response = await Act("tailnet").PostAsJsonAsync("api/tailnet/policy/apply", new { etag = "\"e1\"", digest });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Infra.Calls.Should().ContainSingle(call => call.Action == "policy-apply").Which.Request.Should().Be(("\"e1\"", digest));
        (await LastAudit()).Should().Be(("tailnet.policy.apply", "tailnet", "succeeded"));
        (await Act("tailnet").PostAsJsonAsync("api/tailnet/policy/revert", new { etag = "\"e2\"" })).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await LastAudit()).Should().Be(("tailnet.policy.revert", "tailnet", "succeeded"));
    }

    [Fact]
    public async Task FinishedEnrollment_ShouldMoveTheServerOntoTheTailnetAndAuditAsItsOwner()
    {
        Infra.Finished.Add(new
        {
            id = "6f1c2a9e-0000-4000-8000-000000000001", kind = "enrollment", serverId = "prod-host", status = "succeeded",
            actor = "sulton-max", facts = new { tailnetAddress = "100.101.7.8" },
            mints = new[] { new { keyId = "k1CNTRL", tags = new[] { "tag:products" }, expires = "2026-10-19T12:15:00Z" } },
        });
        Infra.Finished.Add(new
        {
            id = "6f1c2a9e-0000-4000-8000-000000000002", kind = "placement", serverId = "prod-host", product = "foreverpin",
            status = "failed", reason = "Not on the tailnet yet: foreverpin-app", actor = "sulton-max",
        });

        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<InfraJobRecorder>().PassAsync(CancellationToken.None))
                .Should().Be(2);

        var server = (await (await AdminClient.GetAsync("api/servers")).ReadEnvelopeAsync<IReadOnlyList<ServerResponse>>())
            .Single(server => server.Slug == "prod-host");
        (server.Host, server.SshUser, server.SshPort).Should().Be(("100.101.7.8", "wheelhouse-deploy", 22));
        var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Fixture.InventoryRoot, "inventory.json")));
        inventory.RootElement.GetProperty("servers").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "prod-host").GetProperty("host").GetString()
            .Should().Be("100.101.7.8");
        var entries = await Entries();
        var enrolled = entries.Single(entry => entry.GetProperty("action").GetString() == "server.enrollment.finish");
        (enrolled.GetProperty("actor").GetString(), enrolled.GetProperty("outcome").GetString()).Should()
            .Be(("sulton-max", "succeeded"));
        enrolled.GetProperty("detail").GetString().Should().Contain("tailnet 100.101.7.8").And.Contain("k1CNTRL tag:products");
        var placed = entries.Single(entry => entry.GetProperty("action").GetString() == "product.placement.finish");
        (placed.GetProperty("subject").GetString(), placed.GetProperty("outcome").GetString(),
            placed.GetProperty("reason").GetString()).Should()
            .Be(("foreverpin", "failed", "Not on the tailnet yet: foreverpin-app"));
        Infra.Acknowledged.Should().HaveCount(2);
    }

    private const string OwnerKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOwnerKeyOwnerKeyOwnerKey owner@laptop";

    private static object Enrollment() => new
    {
        publicAddress = "203.0.113.20", sshPort = 22, loginUser = "root", admin = "ops", adminKey = OwnerKey,
        tailnetHostname = "ocharo-vps", expectedHostKey = (string?)null,
    };

    private static object Placement() => new
    {
        server = "prod-host", database = true, sites = new[] { new { site = "app", node = "foreverpin-app" } },
    };

    private HttpClient Act(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

    private async Task<List<JsonElement>> Entries()
    {
        var data = await Data(await AdminClient.GetAsync("api/audit?limit=50"));
        return [.. data.EnumerateArray()];
    }

    private async Task<(string?, string?, string?)> LastAudit()
    {
        var entry = (await Entries())[0];
        return (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(),
            entry.GetProperty("outcome").GetString());
    }
}
