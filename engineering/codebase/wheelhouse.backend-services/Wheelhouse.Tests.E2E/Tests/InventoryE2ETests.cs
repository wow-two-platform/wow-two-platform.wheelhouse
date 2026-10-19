using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the inventory the database owns — products, servers, targets and vaults: the owner creates, changes and
/// removes them; a delete never strands a target or a vault; every change reaches the runner's snapshot.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class InventoryE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private static object PilotProduct => new
    {
        slug = "pilot", name = "Pilot", description = "A pilot product.", repository = "owner/pilot", defaultBranch = "main",
        release = new
        {
            asset = "pilot-release.tar.gz", workflow = "publish.yml",
            images = new[] { new { service = "api", image = "ghcr.io/owner/pilot/api" } },
        },
    };

    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private JsonElement Snapshot() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture.InventoryRoot, "inventory.json"))).RootElement;

    [Fact]
    public async Task CreateProduct_ShouldReturn201AndReachTheSnapshot_WhenTheOwnerAddsOne()
    {
        var response = await ActionClient("product").PostJsonAsync("api/products", PilotProduct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith("/api/products/pilot");
        var created = await response.ReadEnvelopeAsync<ProductResponse>();
        (created.Slug, created.Status, created.Environments.Count).Should().Be(("pilot", "active", 0));
        created.InactiveServices.Should().BeEmpty();
        (await AdminClient.GetAsync("api/products/pilot")).StatusCode.Should().Be(HttpStatusCode.OK);

        var pilot = Snapshot().GetProperty("products").EnumerateArray().Single(item => item.GetProperty("slug").GetString() == "pilot");
        pilot.GetProperty("release").GetProperty("images")[0].GetProperty("image").GetString().Should().Be("ghcr.io/owner/pilot/api");
    }

    [Fact]
    public async Task CreateProduct_ShouldReturn409_WhenTheSlugIsTaken()
    {
        var response = await ActionClient("product").PostJsonAsync("api/products",
            new { slug = "foreverpin", name = "Again", repository = "owner/again" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateProduct_ShouldReturn400_WhenAnImageIsMalformed()
    {
        var response = await ActionClient("product").PostJsonAsync("api/products", new
        {
            slug = "broken", name = "Broken", repository = "owner/broken",
            release = new { asset = "broken.tar.gz", images = new[] { new { service = "api", image = "NOT AN IMAGE" } } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Status.Should().Be(400);
    }

    [Fact]
    public async Task UpdateProduct_ShouldReturn200AndKeepTheSlug_WhenTheOwnerChangesIt()
    {
        var response = await ActionClient("product").PutJsonAsync("api/products/wheelhouse",
            new { name = "Wheelhouse console", description = "Control plane.", repository = "wow-two-platform/wow-two-platform.wheelhouse" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.ReadEnvelopeAsync<ProductResponse>();
        (updated.Slug, updated.Name, updated.Description).Should().Be(("wheelhouse", "Wheelhouse console", "Control plane."));
    }

    [Fact]
    public async Task SaveProduct_ShouldStoreWhatDeploysIt_KeepItWhenOmitted_AndRefuseAnUnknownMode()
    {
        var created = await ActionClient("product").PostJsonAsync("api/products",
            new { slug = "pilot", name = "Pilot", repository = "owner/pilot" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("deploy").GetString()
            .Should().Be("wheelhouse", "a new product is deployed by Wheelhouse unless the request says otherwise");

        var ci = await ActionClient("product").PostJsonAsync("api/products",
            new { slug = "selfie", name = "Selfie", repository = "owner/selfie", deploy = "ci" });
        (await ci.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("deploy").GetString().Should().Be("ci");

        (await ProductDeployModes.SetAsync(AdminClient, "pilot", "none")).GetProperty("deploy").GetString().Should().Be("none");
        var kept = await ActionClient("product").PutJsonAsync("api/products/pilot", new { name = "Pilot two", repository = "owner/pilot" });
        (await kept.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("deploy").GetString()
            .Should().Be("none", "a save that names no mode keeps the stored one");

        (await ActionClient("product").PutJsonAsync("api/products/pilot", new { name = "Pilot", repository = "owner/pilot", deploy = "sometimes" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.GetFromJsonAsync<JsonElement>("api/products/wheelhouse")).GetProperty("data").GetProperty("deploy").GetString()
            .Should().Be("ci");
    }

    [Fact]
    public async Task DeleteProduct_ShouldReturn409WhileATargetRunsIt_And204Otherwise()
    {
        (await ActionClient("product").DeleteAsync("api/products/foreverpin")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ActionClient("product").DeleteAsync("api/products/wheelhouse")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AdminClient.GetAsync("api/products/wheelhouse")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Snapshot().GetProperty("products").EnumerateArray().Select(item => item.GetProperty("slug").GetString())
            .Should().Equal("foreverpin");
    }

    [Theory]
    [InlineData("hetzner")]
    [InlineData("local")]
    [InlineData("ovhcloud")]
    public async Task CreateServer_ShouldReturn201AndReachTheSnapshot_WhenTheOwnerAddsOne(string provider)
    {
        var response = await ActionClient("server").PostJsonAsync("api/servers", new
        {
            slug = "extra-host", name = "Extra host", provider, host = "extra.example.net", region = "pilot",
            ingress = new { pattern = "{site}-{product}.{environment}.preview.example" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.ReadEnvelopeAsync<ServerResponse>()).Provider.Should().Be(provider);
        var servers = await (await AdminClient.GetAsync("api/servers")).ReadEnvelopeAsync<IReadOnlyList<ServerResponse>>();
        servers.Single(server => server.Slug == "extra-host").Provider.Should().Be(provider);
        servers.Where(server => server.Slug != "extra-host").Should().OnlyContain(server => server.Provider == "hetzner");
        var snapshotServer = Snapshot().GetProperty("servers").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "extra-host");
        (snapshotServer.GetProperty("provider").GetString(), snapshotServer.GetProperty("sshUser").GetString(),
                snapshotServer.GetProperty("sshPort").GetInt32())
            .Should().Be((provider, "deploy", 22));
        snapshotServer.GetProperty("ingress").GetProperty("entryPoints")[0].GetString().Should().Be("websecure");
    }

    [Fact]
    public async Task DeleteServer_ShouldReturn409_WhileATargetOrAVaultRunsThere()
    {
        (await ActionClient("server").DeleteAsync("api/servers/pilot-host")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ActionClient("target").DeleteAsync("api/targets/foreverpin-dev")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        // The vault still runs there.
        (await ActionClient("server").DeleteAsync("api/servers/pilot-host")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ActionClient("vault").DeleteAsync("api/vaults/pilot-vault")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ActionClient("server").DeleteAsync("api/servers/pilot-host")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreateTarget_ShouldReturn201AndJoinItsProduct_WhenItsProductAndServerExist()
    {
        var response = await ActionClient("target").PostJsonAsync("api/targets", new
        {
            slug = "foreverpin-canary", product = "foreverpin", server = "pilot-host", environment = "dev", network = "platform",
            settings = new[] { new { service = "management", path = "/srv/settings/canary/management.json" } },
            smokeChecks = new[] { new { service = "management", path = "/api/runtime-config", status = 200 } },
            sites = new[] { new { site = "app", host = "canary.foreverpin.example" } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        // Every listing follows the promotion path (dev, prod), then slug — never the alphabet alone.
        var targets = await (await AdminClient.GetAsync("api/targets?product=foreverpin")).ReadEnvelopeAsync<IReadOnlyList<TargetResponse>>();
        targets.Select(target => (target.Slug, target.Environment)).Should()
            .Equal(("foreverpin-canary", "dev"), ("foreverpin-dev", "dev"), ("foreverpin-prod", "prod"));
        var product = await (await AdminClient.GetAsync("api/products/foreverpin")).ReadEnvelopeAsync<ProductResponse>();
        product.Environments.Select(environment => environment.Name).Should().Equal("dev", "dev", "prod");
        Snapshot().GetProperty("targets").EnumerateArray().Select(item => item.GetProperty("id").GetString()).Should()
            .Equal("foreverpin-canary", "foreverpin-dev", "foreverpin-prod");

        var exported = Snapshot().GetProperty("targets").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "foreverpin-canary");
        (exported.GetProperty("environment").GetString(), exported.GetProperty("serverId").GetString(),
                exported.GetProperty("smoke")[0].GetProperty("status").GetInt32())
            .Should().Be(("dev", "pilot-host", 200));
    }

    [Theory]
    [InlineData("\"test\"")]
    [InlineData("1")]
    public async Task Targets_ShouldRefuseTheRetiredTestEnvironment_WhenCreatedOrUpdated(string environment)
    {
        // `test` was retired on 2026-10-17, and its old enum value 1 never stands in for prod.
        static StringContent Body(string? slug, string environment) => new(
            "{" + (slug is null ? "" : "\"slug\":\"" + slug + "\",") + "\"product\":\"foreverpin\",\"server\":\"pilot-host\"," +
            "\"environment\":" + environment + ",\"network\":\"platform\"}", System.Text.Encoding.UTF8, "application/json");

        (await ActionClient("target").PostAsync("api/targets", Body("foreverpin-test", environment))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await ActionClient("target").PutAsync("api/targets/foreverpin-dev", Body(null, environment))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        // The same update passes with a current environment.
        (await ActionClient("target").PutAsync("api/targets/foreverpin-dev", Body(null, "\"dev\""))).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var targets = await (await AdminClient.GetAsync("api/targets?product=foreverpin")).ReadEnvelopeAsync<IReadOnlyList<TargetResponse>>();
        targets.Select(target => (target.Slug, target.Environment)).Should()
            .Equal(("foreverpin-dev", "dev"), ("foreverpin-prod", "prod"));
    }

    [Fact]
    public async Task CreateTarget_ShouldReturn400_WhenItsProductIsUnknown()
    {
        var response = await ActionClient("target").PostJsonAsync("api/targets",
            new { slug = "ghost-dev", product = "ghost", server = "pilot-host", environment = "dev", network = "platform" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTarget_ShouldReturn200_WhenItMovesToAnotherServer()
    {
        var response = await ActionClient("target").PutJsonAsync("api/targets/foreverpin-prod",
            new { product = "foreverpin", server = "pilot-host", environment = "prod", network = "platform", root = "/srv/foreverpin" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.ReadEnvelopeAsync<TargetResponse>();
        (updated.Server, updated.Root).Should().Be(("pilot-host", "/srv/foreverpin"));
    }

    [Fact]
    public async Task Vaults_ShouldBeCreatedListedWithTheirEndpointAndChanged_WhenTheOwnerEditsThem()
    {
        var created = await ActionClient("vault").PostJsonAsync("api/vaults",
            new { slug = "prod-vault", name = "Prod vault", server = "prod-host", url = "http://vault:8080" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var changed = await ActionClient("vault").PutJsonAsync("api/vaults/prod-vault",
            new { name = "Prod vault", server = "prod-host", url = "http://vault.internal:8200" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        var definitions = await (await AdminClient.GetAsync("api/vaults/definitions")).ReadEnvelopeAsync<IReadOnlyList<VaultResponse>>();
        definitions.Should().Equal(
            new VaultResponse("pilot-vault", "Pilot vault", "pilot-host", "http://vault:8080"),
            new VaultResponse("prod-vault", "Prod vault", "prod-host", "http://vault.internal:8200"));
        var product = await (await AdminClient.GetAsync("api/products/foreverpin")).ReadEnvelopeAsync<ProductResponse>();
        product.Environments.Single(environment => environment.Name == "prod").Secrets
            .Should().Be(new ProductSecretsResponse("prod-vault", "foreverpin-prod"));
    }

    [Fact]
    public async Task Writes_ShouldBeRefused_WithoutTheActionHeaderOrWithAnIntegrationKey()
    {
        (await AdminClient.PostJsonAsync("api/products", PilotProduct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.DeleteAsync("api/targets/foreverpin-dev")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var keys = ActionClient("key-create");
        var key = await (await keys.PostJsonAsync("api/integration-keys", new { name = "Agent", scopes = new[] { "catalog:read" } }))
            .ReadEnvelopeAsync<IntegrationKeyWithSecretResponse>();
        var agent = AnonymousClient;
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.Secret);
        agent.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "product");
        (await agent.PostJsonAsync("api/products", PilotProduct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await agent.GetAsync("api/servers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
