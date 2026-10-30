using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for a product's release source as the product form saves it: nobody types it. The owner names a repository, and
/// Wheelhouse derives what the catalog import derives — the asset <c>{slug}-release.tar.gz</c>, the workflow
/// <c>publish-docker-image.yml</c> and one image per service of the repository's <c>deploy.yml</c> at the branch head —
/// shows it before the save (<c>GET api/products/release-source</c>) and stores it on the save. A value a client sends
/// must be the derived one; while the descriptor cannot be read, the images sent or already held stay.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class ProductReleaseSourceE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string Repository = "owner/pilot";
    private const string DescriptorPath = "engineering/deployment/deploy.yml";

    private const string Descriptor =
        """
        descriptor: 1
        product: pilot
        services:
          api:
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health
          web:
            image: ghcr.io/owner/pilot-web
            build: { dockerfile: deployment/web.Dockerfile }
            paths: [codebase/web/**]
            health: /
        """;

    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private static (string, string)[] Images(JsonElement release) =>
    [
        .. release.GetProperty("images").EnumerateArray()
            .Select(image => (image.GetProperty("service").GetString()!, image.GetProperty("image").GetString()!)),
    ];

    private Task<HttpResponseMessage> CreateAsync(object? release) =>
        ActionClient("product").PostJsonAsync("api/products",
            new { slug = "pilot", name = "Pilot", repository = Repository, defaultBranch = "main", release });

    private JsonElement Snapshot() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture.InventoryRoot, "inventory.json"))).RootElement
            .GetProperty("products").EnumerateArray().Single(item => item.GetProperty("slug").GetString() == "pilot");

    [Fact]
    public async Task ReleaseSource_ShouldDeriveTheAssetWorkflowAndImages_FromTheRepository()
    {
        Fixture.GitHub.Push(Repository, DescriptorPath, Descriptor);

        var source = await DataAsync(await AdminClient.GetAsync(
            "api/products/release-source?slug=pilot&repository=owner/pilot&branch=main"));

        (source.GetProperty("asset").GetString(), source.GetProperty("workflow").GetString())
            .Should().Be(("pilot-release.tar.gz", "publish-docker-image.yml"));
        Images(source).Should().Equal(("api", "ghcr.io/owner/pilot/api"), ("web", "ghcr.io/owner/pilot-web"));
        source.GetProperty("fromDescriptor").GetBoolean().Should().BeTrue();
        source.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);
        source.GetProperty("branch").GetString().Should().Be("main");
    }

    [Fact]
    public async Task ReleaseSource_ShouldAnswerWithANote_WhenTheRepositoryCannotBeRead_AndRefuseAMalformedQuery()
    {
        var source = await DataAsync(await AdminClient.GetAsync("api/products/release-source?slug=pilot&repository=owner/missing"));

        source.GetProperty("asset").GetString().Should().Be("pilot-release.tar.gz", "the asset follows the slug, read or not");
        source.GetProperty("images").GetArrayLength().Should().Be(0);
        source.GetProperty("fromDescriptor").GetBoolean().Should().BeFalse();
        source.GetProperty("note").GetString().Should().Contain("owner/missing@main could not be read from GitHub");

        Fixture.GitHub.PushEmpty("owner/empty");
        (await DataAsync(await AdminClient.GetAsync("api/products/release-source?slug=pilot&repository=owner/empty")))
            .GetProperty("note").GetString().Should().Contain("has no engineering/deployment/deploy.yml");

        (await AdminClient.GetAsync("api/products/release-source?slug=pilot&repository=not-a-repository"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AdminClient.GetAsync("api/products/release-source?slug=Pilot&repository=owner/pilot"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AnonymousClient.GetAsync("api/products/release-source?slug=pilot&repository=owner/pilot"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateProduct_ShouldStoreTheDerivedReleaseSource_WhenItAsksForOneWithNothingTyped()
    {
        Fixture.GitHub.Push(Repository, DescriptorPath, Descriptor);

        var created = await DataAsync(await CreateAsync(new { }));

        var release = created.GetProperty("release");
        (release.GetProperty("asset").GetString(), release.GetProperty("workflow").GetString())
            .Should().Be(("pilot-release.tar.gz", "publish-docker-image.yml"));
        Images(release).Should().Equal(("api", "ghcr.io/owner/pilot/api"), ("web", "ghcr.io/owner/pilot-web"));
        Images(Snapshot().GetProperty("release")).Should().Equal(("api", "ghcr.io/owner/pilot/api"), ("web", "ghcr.io/owner/pilot-web"));
    }

    [Fact]
    public async Task CreateProduct_ShouldRefuseATypedValue_ThatDiffersFromWhatTheRepositoryGives()
    {
        Fixture.GitHub.Push(Repository, DescriptorPath, Descriptor);

        var asset = await CreateAsync(new { asset = "wheelhouse-release.tar.gz" });
        asset.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await asset.ReadProblemAsync()).Detail.Should().Be(
            "The release asset of owner/pilot is 'pilot-release.tar.gz', the shared publish workflow's name for 'pilot'; leave it out and Wheelhouse fills it in.");

        var workflow = await CreateAsync(new { workflow = "build.yml" });
        workflow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await workflow.ReadProblemAsync()).Detail.Should().Contain("The build workflow of owner/pilot is 'publish-docker-image.yml'");

        var images = await CreateAsync(new { images = new[] { new { service = "console", image = "ghcr.io/owner/pilot/console" } } });
        images.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await images.ReadProblemAsync()).Detail.Should().Be(
            "deploy.yml in owner/pilot@main names api → ghcr.io/owner/pilot/api, web → ghcr.io/owner/pilot-web; leave the images out and Wheelhouse takes them from it.");
        (await AdminClient.GetAsync("api/products/pilot")).StatusCode.Should().Be(HttpStatusCode.NotFound, "nothing was saved");

        // The derived values themselves, in any order, are what the repository gives.
        (await CreateAsync(new
        {
            asset = "pilot-release.tar.gz", workflow = "publish-docker-image.yml",
            images = new[] { new { service = "web", image = "ghcr.io/owner/pilot-web" }, new { service = "api", image = "ghcr.io/owner/pilot/api" } },
        })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UpdateProduct_ShouldFollowTheDescriptor_AndKeepTheImagesItHad_WhileTheDescriptorCannotBeRead()
    {
        Fixture.GitHub.Push(Repository, DescriptorPath, Descriptor);
        (await CreateAsync(new { })).StatusCode.Should().Be(HttpStatusCode.Created);
        var save = new { name = "Pilot", repository = Repository, defaultBranch = "main", release = new { } };

        // A service the repository adds joins on the next save.
        Fixture.GitHub.Push(Repository, DescriptorPath, Descriptor + "\n  jobs:\n    build: { dockerfile: deployment/jobs.Dockerfile }\n    paths: [codebase/jobs/**]\n");
        Images((await DataAsync(await ActionClient("product").PutJsonAsync("api/products/pilot", save))).GetProperty("release"))
            .Select(image => image.Item1).Should().Equal("api", "web", "jobs");

        // A descriptor that cannot be read keeps what the product had, as the services sync does.
        Fixture.GitHub.Push(Repository, DescriptorPath, FileRead.Failed);
        var kept = (await DataAsync(await ActionClient("product").PutJsonAsync("api/products/pilot", save))).GetProperty("release");
        Images(kept).Select(image => image.Item1).Should().Equal("api", "web", "jobs");
        kept.GetProperty("asset").GetString().Should().Be("pilot-release.tar.gz");
        var shown = await DataAsync(await AdminClient.GetAsync("api/products/release-source?slug=pilot&repository=owner/pilot&branch=main"));
        Images(shown).Select(image => image.Item1).Should().Equal(["api", "web", "jobs"], "the form shows what a save keeps");
        shown.GetProperty("note").GetString().Should().Be(
            "deploy.yml in owner/pilot@main cannot be read or used now, so a save keeps the product's images until it can.");

        // A save without a release source stops publishing.
        (await DataAsync(await ActionClient("product").PutJsonAsync("api/products/pilot",
                new { name = "Pilot", repository = Repository, defaultBranch = "main" })))
            .GetProperty("release").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CreateProduct_ShouldStartWithoutImages_WhenTheRepositoryCannotBeRead_AndKeepTheImagesSent()
    {
        var bare = await DataAsync(await CreateAsync(new { }));

        bare.GetProperty("release").GetProperty("asset").GetString().Should().Be("pilot-release.tar.gz");
        bare.GetProperty("release").GetProperty("images").GetArrayLength().Should().Be(0);

        var sent = await DataAsync(await ActionClient("product").PutJsonAsync("api/products/pilot", new
        {
            name = "Pilot", repository = Repository, defaultBranch = "main",
            release = new { images = new[] { new { service = "api", image = "ghcr.io/owner/pilot/api" } } },
        }));
        Images(sent.GetProperty("release")).Should().Equal(("api", "ghcr.io/owner/pilot/api"));
    }
}
