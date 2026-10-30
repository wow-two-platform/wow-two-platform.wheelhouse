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
/// enrollment, a product's placement and an enrolled server's upkeep reach the runner as the owner asked, are audited,
/// a finished enrollment moves its server onto the tailnet, and a job that brought a host to the kit records its
/// public ingress and public address.</summary>
[Collection(WheelhouseCollection.Name)]
public sealed class InfraAutomationE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private StubInfraGateway Infra => Fixture.Infra;

    [Theory]
    [InlineData("api/tailnet")]
    [InlineData("api/tailnet/policy")]
    [InlineData("api/servers/prod-host/enrollment")]
    [InlineData("api/products/foreverpin/placements")]
    [InlineData("api/products/foreverpin/descriptor")]
    [InlineData("api/servers/prod-host/upkeep")]
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
    public async Task Descriptor_ShouldAnswerTheServicesNeedsAndSitesDeployYmlDeclares()
    {
        var commit = Fixture.RepositoryFiles.Push(Repository, DescriptorPath, Descriptor);

        var data = await Data(await AdminClient.GetAsync("api/products/foreverpin/descriptor"));

        data.GetProperty("commit").GetString().Should().Be(commit);
        data.GetProperty("problem").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("needsPostgres").GetBoolean().Should().BeTrue();
        data.GetProperty("services").EnumerateArray()
            .Select(service => (service.GetProperty("service").GetString(),
                string.Join(",", service.GetProperty("needs").EnumerateArray().Select(need => need.GetString()))))
            .Should().Equal(("management", "postgres"), ("redirect", ""));
        data.GetProperty("sites").EnumerateArray()
            .Select(site => (site.GetProperty("name").GetString(), site.GetProperty("exposure").GetString(),
                site.GetProperty("primary").GetBoolean()))
            .Should().Equal(("app", "public", true), ("ops", "private", false));
        data.GetProperty("extensions").EnumerateArray().Select(extension => extension.GetString())
            .Should().Equal("postgres_fdw", "uuid-ossp");
        (await AdminClient.GetAsync("api/products/missing/descriptor")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Descriptor_ShouldAnswerWhyItCannotBeRead_WhenTheRepositoryHasNone()
    {
        Fixture.RepositoryFiles.PushEmpty(Repository);

        var data = await Data(await AdminClient.GetAsync("api/products/foreverpin/descriptor"));

        data.GetProperty("problem").GetString().Should().Contain("has no engineering/deployment/deploy.yml");
        data.GetProperty("services").GetArrayLength().Should().Be(0);
        data.GetProperty("sites").GetArrayLength().Should().Be(0);
        data.GetProperty("extensions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Place_ShouldStartThePlacementForAKnownProductAndServer()
    {
        Fixture.RepositoryFiles.Push(Repository, DescriptorPath, Descriptor);
        (await Act("placement").PostAsJsonAsync("api/products/missing/placements", Placement())).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var response = await Act("placement").PostAsJsonAsync("api/products/foreverpin/placements", Placement());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = Infra.Calls.Should().ContainSingle(call => call.Action == "place").Subject;
        var request = (ProductPlacementRequest)call.Request!;
        (request.Product, request.Server, request.Database, call.Actor).Should().Be(("foreverpin", "prod-host", true, "test-admin"));
        request.Sites.Should().Equal(new ProductPlacementSite("ops", "foreverpin-ops"));
        // The runner gets what deploy.yml declares, so it checks the request against it too.
        request.Descriptor.NeedsPostgres.Should().BeTrue();
        request.Descriptor.Sites.Select(site => (site.Name, site.Exposure)).Should().Equal(("app", "public"), ("ops", "private"));
        request.Descriptor.Extensions.Should().Equal("postgres_fdw", "uuid-ossp");
        (await LastAudit()).Should().Be(("product.place", "foreverpin", "succeeded"));
    }

    [Theory]
    [InlineData(true, "app", "is public")]
    [InlineData(false, "status", "declares no site 'status'")]
    public async Task Place_ShouldReturn400_WhenASiteIsNotAPrivateSiteOfDeployYml(bool database, string site, string reason)
    {
        Fixture.RepositoryFiles.Push(Repository, DescriptorPath, Descriptor);

        var response = await Act("placement").PostAsJsonAsync("api/products/foreverpin/placements",
            new { server = "prod-host", database, sites = new[] { new { site, node = "foreverpin-" + site } } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Detail.Should().Contain(reason);
        Infra.Calls.Should().NotContain(call => call.Action == "place");
    }

    [Fact]
    public async Task Place_ShouldReturn400_WhenDatabasesAreAskedForAProductWithoutPostgres()
    {
        Fixture.RepositoryFiles.Push(Repository, DescriptorPath, Descriptor.Replace("    needs: [postgres]\n", ""));

        var response = await Act("placement").PostAsJsonAsync("api/products/foreverpin/placements",
            new { server = "prod-host", database = true, sites = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Detail.Should().Contain("needs PostgreSQL");
        Infra.Calls.Should().NotContain(call => call.Action == "place");
    }

    [Fact]
    public async Task Place_ShouldReturn409_WhenDeployYmlCannotBeRead()
    {
        Fixture.RepositoryFiles.PushEmpty(Repository);

        var response = await Act("placement").PostAsJsonAsync("api/products/foreverpin/placements", Placement());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Detail.Should().Contain("follows the product's deploy.yml");
        Infra.Calls.Should().NotContain(call => call.Action == "place");
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

    [Fact]
    public async Task Upkeep_ShouldStartTheJobAsTheOwnerForAKnownServerAndAuditIt()
    {
        (await AdminClient.PostAsync("api/servers/prod-host/upkeep", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Act("server").PostAsync("api/servers/missing/upkeep", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var response = await Act("server").PostAsync("api/servers/prod-host/upkeep", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data(response)).GetProperty("kind").GetString().Should().Be("upkeep");
        var call = Infra.Calls.Should().ContainSingle(call => call.Action == "upkeep").Subject;
        ((string?)call.Request, call.Actor).Should().Be(("prod-host", "test-admin"));
        (await LastAudit()).Should().Be(("server.upkeep", "prod-host", "succeeded"));
        var listed = await Data(await AdminClient.GetAsync("api/servers/prod-host/upkeep"));
        (listed.GetProperty("kit").GetString(), listed.GetProperty("jobs").GetArrayLength()).Should().Be((new string('c', 64), 0));
    }

    [Fact]
    public async Task FinishedUpkeep_ShouldRecordTheKitsPublicIngressAndAddressAndKeepTheOwnersPattern()
    {
        await Act("server").PutAsJsonAsync("api/servers/prod-host", new
        {
            name = "Prod", provider = "hetzner", host = "100.101.7.8", region = "fsn1", sshUser = "wheelhouse-deploy",
            sshPort = 22, ingress = new { scheme = "https", entryPoints = new[] { "private" }, privateEntryPoints = new[] { "private" },
                pattern = "{site}-{product}.{environment}.preview.example" },
        });
        Infra.Finished.Add(new
        {
            id = "6f1c2a9e-0000-4000-8000-000000000003", kind = "upkeep", serverId = "prod-host", status = "succeeded",
            actor = "sulton-max",
            facts = new { kitDigest = new string('d', 64), publicAddress = "203.0.113.20", addressSource = "enrollment", ingress = KitIngress() },
        });

        await Pass();

        var ingress = await IngressOf("prod-host");
        (ingress.GetProperty("certResolver").GetString(), ingress.GetProperty("entryPoints")[0].GetString(),
            ingress.GetProperty("privateEntryPoints")[0].GetString(), ingress.GetProperty("probe").GetString(),
            ingress.GetProperty("publicAddress").GetString(), ingress.GetProperty("pattern").GetString())
            .Should().Be(("letsencrypt", "websecure", "private", "https://127.0.0.1", "203.0.113.20",
                "{site}-{product}.{environment}.preview.example"));
        var exported = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Fixture.InventoryRoot, "inventory.json")))
            .RootElement.GetProperty("servers").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "prod-host");
        exported.GetProperty("ingress").GetProperty("publicAddress").GetString().Should().Be("203.0.113.20");
        var entry = (await Entries()).Single(entry => entry.GetProperty("action").GetString() == "server.upkeep.finish");
        (entry.GetProperty("subject").GetString(), entry.GetProperty("actor").GetString(), entry.GetProperty("outcome").GetString())
            .Should().Be(("prod-host", "sulton-max", "succeeded"));
        entry.GetProperty("detail").GetString().Should().Contain("kit dddddddddddd").And.Contain("public 203.0.113.20");
    }

    [Fact]
    public async Task FinishedEnrollment_ShouldRecordThePublicAddressItEnrolledOn_AndAServerEditKeepsIt()
    {
        Infra.Finished.Add(new
        {
            id = "6f1c2a9e-0000-4000-8000-000000000004", kind = "enrollment", serverId = "prod-host", status = "succeeded",
            actor = "sulton-max",
            facts = new { tailnetAddress = "100.101.7.8", publicAddress = "203.0.113.20", ingress = KitIngress() },
        });
        await Pass();
        (await IngressOf("prod-host")).GetProperty("publicAddress").GetString().Should().Be("203.0.113.20");

        // The address is never typed: an edit that sends another, or none, keeps the recorded one.
        var edited = await Act("server").PutAsJsonAsync("api/servers/prod-host", new
        {
            name = "Prod", provider = "hetzner", host = "100.101.7.8", region = "fsn1", sshUser = "wheelhouse-deploy",
            sshPort = 22, ingress = new { scheme = "https", entryPoints = new[] { "websecure" }, privateEntryPoints = new[] { "private" },
                certResolver = "letsencrypt", publicAddress = "198.51.100.9" },
        });
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        (await IngressOf("prod-host")).GetProperty("publicAddress").GetString().Should().Be("203.0.113.20");
        var created = await Act("server").PostAsJsonAsync("api/servers", new
        {
            slug = "spare-host", name = "Spare", provider = "contabo", host = "198.51.100.10", region = "eu",
            ingress = new { scheme = "https", entryPoints = new[] { "websecure" }, privateEntryPoints = Array.Empty<string>(),
                publicAddress = "198.51.100.10" },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await IngressOf("spare-host")).GetProperty("publicAddress").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task FinishedUpkeep_ShouldRecordNothing_WhenItsIngressIsNotOneWheelhouseCanUse()
    {
        Infra.Finished.Add(new
        {
            id = "6f1c2a9e-0000-4000-8000-000000000005", kind = "upkeep", serverId = "prod-host", status = "succeeded",
            actor = "sulton-max",
            facts = new { publicAddress = "203.0.113.20", ingress = new { scheme = "ftp", entryPoints = new[] { "web secure" },
                privateEntryPoints = Array.Empty<string>() } },
        });

        await Pass();

        (await IngressOf("prod-host")).GetProperty("publicAddress").ValueKind.Should().Be(JsonValueKind.Null);
        (await Entries()).Single(entry => entry.GetProperty("action").GetString() == "server.upkeep.finish")
            .GetProperty("outcome").GetString().Should().Be("failed");
    }

    private static object KitIngress() => new
    {
        scheme = "https", port = (int?)null, entryPoints = new[] { "websecure" }, privateEntryPoints = new[] { "private" },
        certResolver = "letsencrypt", pattern = (string?)null, probe = "https://127.0.0.1", privateProbe = "http://127.0.0.1:18080",
    };

    private async Task Pass()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InfraJobRecorder>().PassAsync(CancellationToken.None);
    }

    private async Task<JsonElement> IngressOf(string slug) =>
        (await Data(await AdminClient.GetAsync("api/servers"))).EnumerateArray()
            .Single(server => server.GetProperty("slug").GetString() == slug).GetProperty("ingress");

    private const string OwnerKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOwnerKeyOwnerKeyOwnerKey owner@laptop";

    private static object Enrollment() => new
    {
        publicAddress = "203.0.113.20", sshPort = 22, loginUser = "root", admin = "ops", adminKey = OwnerKey,
        tailnetHostname = "ocharo-vps", expectedHostKey = (string?)null,
    };

    private static object Placement() => new
    {
        server = "prod-host", database = true, sites = new[] { new { site = "ops", node = "foreverpin-ops" } },
    };

    private const string Repository = "sulton-max/10x-venture-forever-pin";

    private const string DescriptorPath = "engineering/deployment/deploy.yml";

    /// <summary>A descriptor 1 product: management needs PostgreSQL and serves the public app site and the private ops
    /// site; redirect needs nothing. Its databases take two untrusted extensions.</summary>
    private const string Descriptor = """
        descriptor: 1
        product: foreverpin
        extensions: [postgres_fdw, "uuid-ossp"]
        services:
          management:
            needs: [postgres]
            sites:
              app: { exposure: public }
              ops: { exposure: private }
          redirect:
            sites:
              app: {}

        """;

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
        return [.. (await AdminClient.AuditItemsAsync("?pageSize=50")).EnumerateArray()];
    }

    private async Task<(string?, string?, string?)> LastAudit()
    {
        var entry = (await Entries())[0];
        return (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(),
            entry.GetProperty("outcome").GetString());
    }
}
