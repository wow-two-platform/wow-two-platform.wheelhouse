using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Automation.Services;
using Wheelhouse.Tests.E2E.Harness;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for a product built from several repositories (decision 70). A catalog entry listing several repositories
/// imports as one product whose primary repository comes first and whose further repositories each carry a release source
/// derived from their own <c>deploy.yml</c>; a service two repositories declare refuses it. The entry's <c>replaces</c>
/// retires the products it supersedes when nothing ever deployed to them. The product form takes further repositories,
/// the descriptor read merges every repository's services and sites, and branch, commit and build reads name the
/// repository. The approved services follow each repository's <c>deploy.yml</c> after the import too: a refresh, the
/// release catalog and the release watcher take a service a repository added and a repository's first descriptor. The
/// runner composes the release sets; its own tests cover them.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class MultiRepoProductE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string Hub = "ocharo-hq/ocharo-ws";
    private const string Platform = "ocharo-hq/ocharo-platform";
    private const string Studio = "ocharo-hq/ocharo-studio";
    private const string Assets = "ocharo-hq/ocharo-assets";
    private const string Marketing = "ocharo-hq/ocharo-marketing";

    /// <summary>The Ocharo hub as the merge leaves it: one product of three repositories that replaces two older ones.</summary>
    private const string Catalog =
        """
        catalog: 1
        hub:
          slug: ocharo
          name: Ocharo
          repository: ocharo-hq/ocharo-ws
        workspaces:
          - slug: ocharo
            name: Ocharo
            description: "Maftuna's brand"
        entries:
          - slug: ocharo
            name: Ocharo
            workspace: ocharo
            kind: venture
            deploy: wheelhouse
            purpose: "Ocharo: Desk, Studio and the motion lab under one brand and domain"
            repositories: [ocharo-hq/ocharo-platform, ocharo-hq/ocharo-studio, ocharo-hq/ocharo-assets]
            replaces: [ocharo-platform, ocharo-studio]
          - slug: ocharo-hq
            name: ocharo-hq
            workspace: ocharo
            kind: core
            deploy: none
            purpose: "Main brand overview"
            repositories: [ocharo-hq/ocharo-hq]
        """;

    private const string PlatformDescriptor =
        """
        descriptor: 2
        product: ocharo
        sites:
          desk: {}
        services:
          desk:
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health/ready
            needs: [postgres]
            sites:
              desk: {}
          jobs:
            build: { dockerfile: deployment/Dockerfile.worker }
            paths: [codebase/**]
            health: { command: [sh, -c, 'kill -0 1'] }
            needs: [postgres]
            dependsOn: [desk]
        """;

    private const string StudioDescriptor =
        """
        descriptor: 2
        product: ocharo
        sites:
          studio: {}
        services:
          studio:
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health
            sites:
              studio: {}
        """;

    private const string AssetsDescriptor =
        """
        descriptor: 2
        product: ocharo
        sites:
          motion: { exposure: private }
        services:
          motion:
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health
            sites:
              motion: {}
        """;

    /// <summary>Marketing's first deploy.yml: two services, one site.</summary>
    private const string MarketingDescriptor =
        """
        descriptor: 2
        product: ocharo
        sites:
          marketing: {}
        services:
          marketing:
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health
            sites:
              marketing: {}
          blog:
            build: { dockerfile: deployment/Dockerfile.blog }
            paths: [codebase/**]
            health: /health
        """;

    /// <summary>Two services Platform adds after the import; one names its own image.</summary>
    private const string PlatformAdditions =
        """

          brand:
            build: { dockerfile: deployment/Dockerfile.brand }
            paths: [codebase/**]
            health: /health
          catalogue:
            image: ghcr.io/ocharo-hq/catalogue-edge
            build: { dockerfile: deployment/Dockerfile.catalogue }
            paths: [codebase/**]
            health: /health
        """;

    private const string Descriptor = "engineering/deployment/deploy.yml";

    private HttpClient ActionClient(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private void PushRepositories(string studio = StudioDescriptor)
    {
        Fixture.RepositoryFiles.Push(Hub, "catalog.yml", Catalog);
        Fixture.RepositoryFiles.Push(Platform, "engineering/deployment/deploy.yml", PlatformDescriptor);
        Fixture.RepositoryFiles.Push(Studio, "engineering/deployment/deploy.yml", studio);
        Fixture.RepositoryFiles.Push(Assets, "engineering/deployment/deploy.yml", AssetsDescriptor);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

    private async Task<JsonElement> ProductAsync(string slug) => await DataAsync(await AdminClient.GetAsync("api/products/" + slug));

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    private JsonElement Snapshot() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture.InventoryRoot, "inventory.json"))).RootElement;

    private static (string?, string?)[] Images(JsonElement repository) =>
        [.. repository.GetProperty("release").GetProperty("images").EnumerateArray()
            .Select(item => (item.GetProperty("service").GetString(), item.GetProperty("image").GetString()))];

    private string?[] ExportedServices(int repository)
    {
        var exported = Snapshot().GetProperty("products").EnumerateArray().Single(item => item.GetProperty("slug").GetString() == "ocharo");
        var release = repository == 0
            ? exported.GetProperty("release")
            : exported.GetProperty("components")[repository - 1].GetProperty("release");
        return [.. release.GetProperty("images").EnumerateArray().Select(item => item.GetProperty("service").GetString())];
    }

    private async Task<JsonElement[]> ServiceChangesAsync() =>
    [
        .. (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "product.services"),
    ];

    /// <summary>Imports Ocharo with Marketing as its last repository, before Marketing has a deploy.yml.</summary>
    private async Task ImportWithMarketingAsync()
    {
        PushRepositories();
        Fixture.RepositoryFiles.Push(Hub, "catalog.yml", Catalog.Replace(Assets + "]", Assets + ", " + Marketing + "]"));
        Fixture.RepositoryFiles.PushEmpty(Marketing);
        (await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } })).EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> RefreshAsync() =>
        await ActionClient("refresh").PostAsync("api/products/ocharo/identity/refresh", null);

    private async Task CreateProductAsync(string slug, string repository)
    {
        (await ActionClient("product").PostJsonAsync("api/products", new { slug, name = slug, repository }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task CreateTargetAsync(string slug, string product)
    {
        (await ActionClient("target").PostJsonAsync("api/targets", new
        {
            slug, product, server = "pilot-host", environment = "dev", network = "platform",
        })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Import_ShouldMakeOneProductOfEveryRepository_AndRetireTheProductsItReplaces()
    {
        PushRepositories();
        await CreateProductAsync("ocharo-platform", Platform);
        await CreateProductAsync("ocharo-studio", Studio);
        await CreateTargetAsync("ocharo-studio-dev", "ocharo-studio");

        var response = await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await DataAsync(response);
        Strings(result.GetProperty("createdProducts")).Should().Equal("ocharo");
        Strings(result.GetProperty("retiredProducts")).Should().Equal("ocharo-platform", "ocharo-studio");
        result.GetProperty("warnings").GetArrayLength().Should().Be(0);
        (await AdminClient.GetAsync("api/products/ocharo-studio")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AdminClient.GetAsync("api/products/ocharo-platform")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Snapshot().GetProperty("targets").EnumerateArray().Select(item => item.GetProperty("id").GetString())
            .Should().NotContain("ocharo-studio-dev");

        var ocharo = await ProductAsync("ocharo");
        (ocharo.GetProperty("name").GetString(), ocharo.GetProperty("repository").GetProperty("name").GetString())
            .Should().Be(("Ocharo", Platform));
        var repositories = ocharo.GetProperty("repositories").EnumerateArray().ToList();
        repositories.Select(item => (item.GetProperty("name").GetString(), item.GetProperty("repository").GetString(),
                item.GetProperty("release").GetProperty("asset").GetString()))
            .Should().Equal(("platform", Platform, "ocharo-release.tar.gz"), ("studio", Studio, "ocharo-release.tar.gz"),
                ("assets", Assets, "ocharo-release.tar.gz"));
        repositories[1].GetProperty("release").GetProperty("images")[0].GetProperty("image").GetString()
            .Should().Be("ghcr.io/ocharo-hq/ocharo-studio/studio");

        // The runner's snapshot carries the further repositories with their own release sources.
        var exported = Snapshot().GetProperty("products").EnumerateArray().Single(item => item.GetProperty("slug").GetString() == "ocharo");
        exported.GetProperty("components").EnumerateArray().Select(item => item.GetProperty("repository").GetString())
            .Should().Equal(Studio, Assets);
        exported.GetProperty("release").GetProperty("images").EnumerateArray().Select(item => item.GetProperty("service").GetString())
            .Should().Equal("desk", "jobs");

        // The catalog lists what the entry replaces, and a second import changes nothing more.
        var entry = (await DataAsync(await AdminClient.GetAsync("api/catalog"))).GetProperty("entries").EnumerateArray()
            .Single(item => item.GetProperty("slug").GetString() == "ocharo");
        Strings(entry.GetProperty("replaces")).Should().Equal("ocharo-platform", "ocharo-studio");
        var again = await DataAsync(await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } }));
        (Strings(again.GetProperty("createdProducts")), Strings(again.GetProperty("updatedProducts")), Strings(again.GetProperty("retiredProducts")))
            .Should().BeEquivalentTo((Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public async Task Import_ShouldKeepAReplacedProductThatHasDeploys_WithAWarning()
    {
        PushRepositories();
        await CreateProductAsync("ocharo-studio", Studio);
        await CreateTargetAsync("ocharo-studio-dev", "ocharo-studio");
        Fixture.Deployments.History.Add(new
        {
            id = "00000000-0000-4000-8000-000000000070", targetId = "ocharo-studio-dev", bundleId = "ocharo-studio-gh-1",
            release = "v0.1.6", status = "succeeded", submittedAt = "2026-10-20T10:00:00+00:00",
        });

        var result = await DataAsync(await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } }));

        Strings(result.GetProperty("createdProducts")).Should().Equal("ocharo");
        Strings(result.GetProperty("retiredProducts")).Should().BeEmpty();
        var warning = result.GetProperty("warnings").EnumerateArray().Single();
        (warning.GetProperty("slug").GetString(), warning.GetProperty("message").GetString())
            .Should().Be(("ocharo-studio",
                "'ocharo' replaces it, but it has deploys on record, so it stays; remove its environments and the product by hand once it is torn down."));
        (await AdminClient.GetAsync("api/products/ocharo-studio")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Import_ShouldCreateNothing_WhenTwoRepositoriesDeclareOneService()
    {
        PushRepositories(StudioDescriptor.Replace("  studio:\n    build", "  desk:\n    build"));

        var result = await DataAsync(await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } }));

        Strings(result.GetProperty("createdProducts")).Should().BeEmpty();
        result.GetProperty("warnings")[0].GetProperty("message").GetString().Should().Be(
            "Service 'desk' is in both ocharo-hq/ocharo-platform and ocharo-hq/ocharo-studio; a service name belongs to one repository of the product. No product was created.");
        (await AdminClient.GetAsync("api/products/ocharo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_ShouldNoteARepositoryWhoseDescriptorStillNamesItsOldProduct()
    {
        PushRepositories(StudioDescriptor.Replace("product: ocharo", "product: ocharo-studio"));

        var result = await DataAsync(await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } }));

        result.GetProperty("warnings")[0].GetProperty("message").GetString().Should().Be(
            "deploy.yml in ocharo-hq/ocharo-studio@main names the product 'ocharo-studio', not 'ocharo'; every repository of 'ocharo' must name it before its releases join the product's.");
    }

    [Fact]
    public async Task Import_ShouldNoteThatARepositoryWithoutADescriptorAddsNoServicesYet()
    {
        PushRepositories();
        Fixture.RepositoryFiles.Push(Hub, "catalog.yml", Catalog.Replace(Assets + "]", Assets + ", " + Marketing + "]"));
        Fixture.RepositoryFiles.PushEmpty(Marketing);

        var result = await DataAsync(await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } }));

        Strings(result.GetProperty("createdProducts")).Should().Equal("ocharo");
        result.GetProperty("warnings").EnumerateArray().Select(item => item.GetProperty("message").GetString()).Should().Equal(
            "ocharo-hq/ocharo-marketing@main has no engineering/deployment/deploy.yml yet, so it adds no services until it has one and releases.");
        var marketing = (await ProductAsync("ocharo")).GetProperty("repositories").EnumerateArray().Last();
        (marketing.GetProperty("name").GetString(), marketing.GetProperty("release").GetProperty("images").GetArrayLength())
            .Should().Be(("marketing", 0));
    }

    [Fact]
    public async Task Import_ShouldNoteThatThePrimaryRepositoryWithoutADescriptorHoldsBackEverySet()
    {
        PushRepositories();
        Fixture.RepositoryFiles.PushEmpty(Platform);

        var result = await DataAsync(await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } }));

        result.GetProperty("warnings").EnumerateArray().Select(item => item.GetProperty("message").GetString()).Should().Equal(
            "ocharo-hq/ocharo-platform@main has no engineering/deployment/deploy.yml yet; it serves the product's front door, so the product has no release sets until it has one and releases.");
    }

    [Fact]
    public async Task Refresh_ShouldApproveWhatEachRepositorysDeployYmlNamesNow_WithoutAReimport()
    {
        await ImportWithMarketingAsync();
        Fixture.RepositoryFiles.Push(Platform, Descriptor, PlatformDescriptor + PlatformAdditions);
        Fixture.RepositoryFiles.Push(Marketing, Descriptor, MarketingDescriptor);

        var response = await RefreshAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var repositories = (await DataAsync(response)).GetProperty("repositories").EnumerateArray().ToList();
        Images(repositories[0]).Should().Equal(
            ("desk", "ghcr.io/ocharo-hq/ocharo-platform/desk"), ("jobs", "ghcr.io/ocharo-hq/ocharo-platform/jobs"),
            ("brand", "ghcr.io/ocharo-hq/ocharo-platform/brand"), ("catalogue", "ghcr.io/ocharo-hq/catalogue-edge"));
        Images(repositories[3]).Should().Equal(
            ("marketing", "ghcr.io/ocharo-hq/ocharo-marketing/marketing"), ("blog", "ghcr.io/ocharo-hq/ocharo-marketing/blog"));
        repositories[3].GetProperty("release").GetProperty("asset").GetString().Should().Be("ocharo-release.tar.gz");

        // The runner's snapshot carries them, so its release sets take Marketing and Platform's new services.
        ExportedServices(0).Should().Equal("desk", "jobs", "brand", "catalogue");
        ExportedServices(3).Should().Equal("marketing", "blog");
        // The environment form's services follow: Marketing deploys something now.
        (await DataAsync(await AdminClient.GetAsync("api/products/ocharo/descriptor"))).GetProperty("services").EnumerateArray()
            .Select(item => item.GetProperty("service").GetString())
            .Should().Equal("desk", "jobs", "brand", "catalogue", "studio", "motion", "marketing", "blog");
        var change = (await ServiceChangesAsync()).Should().ContainSingle().Subject;
        (change.GetProperty("subject").GetString(), change.GetProperty("detail").GetString())
            .Should().Be(("ocharo", "platform +brand +catalogue; marketing +marketing +blog"));

        // Nothing new reads as no change; a removed service leaves.
        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ServiceChangesAsync()).Should().ContainSingle();
        Fixture.RepositoryFiles.Push(Platform, Descriptor, PlatformDescriptor.Replace("    dependsOn: [desk]", "").Replace("  jobs:\n", "  render:\n"));
        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        ExportedServices(0).Should().Equal("desk", "render");
        (await ServiceChangesAsync())[0].GetProperty("detail").GetString().Should().Be("platform +render -jobs -brand -catalogue");
    }

    [Fact]
    public async Task ReleaseCatalogAndWatcher_ShouldTakeANewServiceBeforeTheRunnerComposesSets()
    {
        await ImportWithMarketingAsync();
        Fixture.RepositoryFiles.Push(Marketing, Descriptor, MarketingDescriptor);

        (await AdminClient.GetAsync("api/deployments/releases")).StatusCode.Should().Be(HttpStatusCode.OK);

        ExportedServices(3).Should().Equal("marketing", "blog");
        Fixture.RepositoryFiles.Push(Studio, Descriptor, StudioDescriptor.Replace("  studio:\n    build", "  gallery:\n    build"));
        await using (var scope = Fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DeploymentAutomationService>().RunOnceAsync(CancellationToken.None);
        ExportedServices(1).Should().Equal("gallery");
        (await ServiceChangesAsync()).Select(entry => (entry.GetProperty("actor").GetString(), entry.GetProperty("detail").GetString()))
            .Should().Equal(("automation", "studio +gallery -studio"), (TestAuthExtensions.AdminLogin, "marketing +marketing +blog"));
    }

    [Fact]
    public async Task Refresh_ShouldKeepARepositorysServices_WhenItsDescriptorIsUnusableOrTakesAnotherRepositorysService()
    {
        await ImportWithMarketingAsync();
        Fixture.RepositoryFiles.Push(Studio, Descriptor, RepositoryFileStatus.Unreadable);
        Fixture.RepositoryFiles.Push(Platform, Descriptor, PlatformDescriptor + PlatformAdditions);

        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);

        ExportedServices(0).Should().Equal("desk", "jobs", "brand", "catalogue");
        ExportedServices(1).Should().Equal("studio");
        ExportedServices(3).Should().BeEmpty();

        // Marketing naming Platform's service would put one service in two repositories: the product stays as it was.
        Fixture.RepositoryFiles.Push(Marketing, Descriptor, MarketingDescriptor.Replace("  blog:", "  brand:"));
        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        ExportedServices(3).Should().BeEmpty();
        (await ServiceChangesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Import_ShouldGiveAnExistingProductTheEntrysRepositories()
    {
        PushRepositories();
        await CreateProductAsync("ocharo", Platform);

        var result = await DataAsync(await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } }));

        Strings(result.GetProperty("updatedProducts")).Should().Equal("ocharo");
        (await ProductAsync("ocharo")).GetProperty("repositories").EnumerateArray().Select(item => item.GetProperty("name").GetString())
            .Should().Equal("platform", "studio", "assets");
    }

    [Fact]
    public async Task Descriptor_ShouldMergeEveryRepositorysServicesAndSites_WithThePrimaryFrontDoor()
    {
        PushRepositories();
        (await ActionClient("catalog").PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo" } })).EnsureSuccessStatusCode();

        var descriptor = await DataAsync(await AdminClient.GetAsync("api/products/ocharo/descriptor"));

        descriptor.GetProperty("problem").ValueKind.Should().Be(JsonValueKind.Null);
        descriptor.GetProperty("services").EnumerateArray().Select(item => item.GetProperty("service").GetString())
            .Should().Equal("desk", "jobs", "studio", "motion");
        descriptor.GetProperty("sites").EnumerateArray()
            .Select(item => (item.GetProperty("name").GetString(), item.GetProperty("exposure").GetString(), item.GetProperty("primary").GetBoolean()))
            .Should().Equal(("desk", "public", true), ("studio", "public", false), ("motion", "private", false));
        descriptor.GetProperty("needsPostgres").GetBoolean().Should().BeTrue();
        descriptor.GetProperty("extensions").GetArrayLength().Should().Be(0);

        // Extensions belong to the product's databases: every repository's, once each, in first-seen order.
        Fixture.RepositoryFiles.Push(Platform, Descriptor,
            PlatformDescriptor.Replace("product: ocharo\n", "product: ocharo\nextensions: [postgres_fdw, uuid-ossp]\n"));
        Fixture.RepositoryFiles.Push(Studio, Descriptor, StudioDescriptor + "\nextensions: [uuid-ossp, pg_trgm]\n");
        (await DataAsync(await AdminClient.GetAsync("api/products/ocharo/descriptor"))).GetProperty("extensions")
            .EnumerateArray().Select(item => item.GetString()).Should().Equal("postgres_fdw", "uuid-ossp", "pg_trgm");
        Fixture.RepositoryFiles.Push(Platform, Descriptor, PlatformDescriptor);

        // Two repositories naming one site is a problem the placement and environment forms show.
        Fixture.RepositoryFiles.Push(Studio, "engineering/deployment/deploy.yml", StudioDescriptor.Replace("studio: {}", "desk: {}"));
        (await DataAsync(await AdminClient.GetAsync("api/products/ocharo/descriptor"))).GetProperty("problem").GetString()
            .Should().Be("Site 'desk' is in both ocharo-hq/ocharo-platform and ocharo-hq/ocharo-studio; a site names one host role of the product, so each repository names its own.");
    }

    [Fact]
    public async Task ProductForm_ShouldTakeFurtherRepositories_AndRefuseAServiceTwoOfThemDeclare()
    {
        static object Release(string repository, params string[] services) => new
        {
            asset = "pilot-release.tar.gz", workflow = "publish-docker-image.yml",
            images = services.Select(service => new { service, image = "ghcr.io/" + repository + "/" + service }).ToArray(),
        };

        var created = await ActionClient("product").PostJsonAsync("api/products", new
        {
            slug = "pilot", name = "Pilot", repository = "owner/pilot-app", release = Release("owner/pilot-app", "api"),
            components = new[] { new { repository = "owner/pilot-studio", release = Release("owner/pilot-studio", "studio") } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await DataAsync(created)).GetProperty("repositories").EnumerateArray().Select(item => item.GetProperty("name").GetString())
            .Should().Equal("app", "studio");

        // A save without components keeps them; one that repeats a service across repositories is refused.
        (await ActionClient("product").PutAsJsonAsync("api/products/pilot", new
        {
            name = "Pilot", repository = "owner/pilot-app", release = Release("owner/pilot-app", "api", "web"),
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ProductAsync("pilot")).GetProperty("repositories").GetArrayLength().Should().Be(2);
        var clash = await ActionClient("product").PutAsJsonAsync("api/products/pilot", new
        {
            name = "Pilot", repository = "owner/pilot-app", release = Release("owner/pilot-app", "api"),
            components = new[] { new { repository = "owner/pilot-studio", release = Release("owner/pilot-studio", "api") } },
        });
        clash.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await clash.ReadProblemAsync()).Detail.Should().Contain("Service 'api' is in both owner/pilot-app and owner/pilot-studio");
        var twice = await ActionClient("product").PostJsonAsync("api/products", new
        {
            slug = "pilot-two", name = "Pilot", repository = "owner/pilot-app",
            components = new[] { new { repository = "Owner/Pilot-App" } },
        });
        twice.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await twice.ReadProblemAsync()).Detail.Should().Be("A product names each repository once.");
        (await ActionClient("product").PostJsonAsync("api/products", new
        {
            slug = "pilot-three", name = "Pilot", repository = "owner/pilot-app", components = new[] { new { repository = "not a repository" } },
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BuildsAndChangelogs_ShouldNameTheRepository_AndTakeReleaseSetLabels()
    {
        (await AdminClient.GetAsync("api/deployments/products/foreverpin/branches?repository=ocharo-hq/ocharo-studio"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Deployments.LastRepository.Should().Be(Studio);
        (await AdminClient.GetAsync("api/deployments/products/foreverpin/commits?branch=main&repository=ocharo-hq/ocharo-assets"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Deployments.LastRepository.Should().Be(Assets);
        var build = ActionClient("build");
        (await build.PostJsonAsync("api/deployments/products/foreverpin/builds",
            new { commit = new string('c', 40), repository = Studio })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (Fixture.Deployments.LastBuild, Fixture.Deployments.LastRepository).Should().Be((("foreverpin", new string('c', 40)), Studio));
        (await AdminClient.GetAsync("api/deployments/products/foreverpin/commits?branch=main&repository=not-a-repository"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var changelog = await AdminClient.GetAsync(
            "api/deployments/products/ocharo/changelog?from=platform-v0.1.8_studio-v0.1.6&to=platform-v0.1.9_studio-sha-abcdef1");
        changelog.StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Deployments.LastChangelog.Should().Be(("ocharo", "platform-v0.1.8_studio-v0.1.6", "platform-v0.1.9_studio-sha-abcdef1"));
        (await AdminClient.GetAsync("api/deployments/products/ocharo/changelog?to=set-0123456789ab")).StatusCode.Should().Be(HttpStatusCode.OK);
        // A set the repositories that have not released yet are left out of: here the primary one alone.
        (await AdminClient.GetAsync("api/deployments/products/ocharo/changelog?from=platform-v0.1.8&to=platform-v0.1.9_assets-v0.1.1"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Deployments.LastChangelog.Should().Be(("ocharo", "platform-v0.1.8", "platform-v0.1.9_assets-v0.1.1"));
        (await AdminClient.GetAsync("api/deployments/products/ocharo/changelog?to=platform_v1")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }
}
