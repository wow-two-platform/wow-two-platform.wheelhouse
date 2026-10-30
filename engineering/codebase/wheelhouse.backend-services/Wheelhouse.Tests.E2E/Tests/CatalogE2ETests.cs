using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Tests.E2E.Harness;
using Wheelhouse.Tests.E2E.Support;
using WoW.Two.Sdk.Beta.Be.Testing.Web;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for the catalog hubs — Wheelhouse reads <c>catalog.yml</c> from each hub <c>Catalog:Sources</c> lists
/// (<c>wow-two/wow-two-ws</c>, <c>sulton-max/10x-ventures</c>, <c>ocharo-hq/ocharo-ws</c>, each at <c>main</c>; the stub
/// answers for GitHub), merges them in that order with each entry tagged by its hub, and imports entries from any of
/// them: workspaces as needed, a product per entry Wheelhouse deploys with images derived from its <c>deploy.yml</c>,
/// and moves for existing products. A hub that cannot be read, is malformed or repeats an earlier hub's slug carries its
/// error, naming the line and field or both hubs, and lists nothing; the other hubs still list theirs.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class CatalogE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string Wow2 = "wow-two/wow-two-ws";
    private const string Ventures = "sulton-max/10x-ventures";
    private const string Ocharo = "ocharo-hq/ocharo-ws";

    /// <summary>The wow2 hub's catalog, shaped like the real one: registry-only keys, quoted free text, block workspaces
    /// and entries with and without repositories.</summary>
    private const string Catalog =
        """
        # yaml-language-server: $schema=conventions/development/repo/catalog/catalog.schema.json
        catalog: 1
        hub:
          slug: wow2
          name: wow2
          repository: wow-two/wow-two-ws
        hubs:
          - repository: sulton-max/10x-ventures
            local: workbench/ventures/10x-ventures

        orgs:
          - slug: wow-two-platform
            url: https://github.com/wow-two-platform
            label: internal infra
            description: "pipelines, DI, comms"
            intro:
              - "Some **Markdown** intro."
            notes:
              - "> A note."

        workspaces:
          - slug: personal
            name: Personal
            description: "Personal tools and documents outside the ecosystem"
          - slug: wow2-platform
            name: wow2 platform
            description: "Platform apps that deploy and run the portfolio"

        entries:
          - slug: wheelhouse
            name: Wheelhouse
            workspace: wow2-platform
            kind: platform
            deploy: ci
            status: "Active, v0.5"
            purpose: "Product ops and deploy control plane"
            repositories: [wow-two-platform/wow-two-platform.wheelhouse]
          - slug: secrets-vault
            name: Secrets Vault
            workspace: wow2-platform
            kind: platform
            deploy: wheelhouse
            status: "Active"
            purpose: "Secrets vault — envelope-encrypted store + management UI"
            repositories: [wow-two-platform/wow-two-platform.secrets-vault]
          - slug: wheelhousecanary
            name: Wheelhouse Canary
            workspace: wow2-platform
            kind: platform
            deploy: wheelhouse
            status: "v0.1 built"
            purpose: "Sample app Wheelhouse deploys to prove every flow"
            repositories: [wow-two-platform/wow-two-platform.wheelhouse-canary]
          - slug: notes-app
            name: Notes
            workspace: personal
            kind: app
            deploy: wheelhouse
            status: "Idea"
            purpose: "Notes kept locally"
            repositories: []
            local: workbench/personal/notes-app
          - slug: pipelines
            name: wow-two-platform.pipelines
            workspace: wow2-platform
            kind: backbone
            deploy: none
            status: "Active"
            purpose: "Shared publish workflow"
            repositories: [wow-two-platform/wow-two-platform.pipelines]
        """;

    /// <summary>The ventures hub's catalog: no orgs, two workspaces.</summary>
    private const string VenturesCatalog =
        """
        catalog: 1
        hub:
          slug: ventures
          name: 10x Ventures
          repository: sulton-max/10x-ventures
        workspaces:
          - slug: own-ventures
            name: Own ventures
            description: "The owner's product ventures"
          - slug: other-ventures
            name: Other ventures
            description: "Ventures and client work run for others"
        entries:
          - slug: foreverpin
            name: ForeverPin
            workspace: own-ventures
            kind: venture
            deploy: wheelhouse
            status: "Active, v0.9"
            purpose: "ForeverPin QR and programmable links"
            repositories: [sulton-max/10x-venture-forever-pin]
            local: workbench/ventures/10x-venture-forever-pin
          - slug: haven
            name: Haven
            workspace: other-ventures
            kind: venture
            deploy: wheelhouse
            status: "Active"
            purpose: "Real estate platform"
            repositories: [sulton-max/10x-ven-haven]
        """;

    /// <summary>The Ocharo hub's catalog: one org, one workspace.</summary>
    private const string OcharoCatalog =
        """
        catalog: 1
        hub:
          slug: ocharo
          name: Ocharo
          repository: ocharo-hq/ocharo-ws
        orgs:
          - slug: ocharo-hq
            url: https://github.com/ocharo-hq
            description: "Ocharo's brand organization"
        workspaces:
          - slug: ocharo
            name: Ocharo
            description: "Maftuna's brand"
        entries:
          - slug: ocharo-platform
            name: Ocharo Platform
            workspace: ocharo
            kind: venture
            deploy: wheelhouse
            status: "Integration pending"
            purpose: "Brand identity, catalogue and business management"
            repositories: [ocharo-hq/ocharo-platform]
        """;

    private const string CanaryDescriptor =
        """
        descriptor: 1
        product: wheelhousecanary
        services:
          api:
            build: { dockerfile: deployment/Dockerfile }
            paths: [codebase/**]
            health: /health
          web:
            image: ghcr.io/wow-two-platform/canary-web
            build: { dockerfile: deployment/web.Dockerfile }
            paths: [codebase/web/**]
            health: /
        """;

    private HttpClient ImportClient()
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", "catalog");
        return client;
    }

    private string PushCatalog(string text = Catalog, string repository = Wow2) => Fixture.RepositoryFiles.Push(repository, "catalog.yml", text);

    /// <summary>Pushes all three hubs' catalogs and returns their commits, in settings order.</summary>
    private (string Wow2, string Ventures, string Ocharo) PushHubs(string ventures = VenturesCatalog, string ocharo = OcharoCatalog) =>
        (PushCatalog(), PushCatalog(ventures, Ventures), PushCatalog(ocharo, Ocharo));

    private async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

    private async Task<JsonElement> CatalogAsync()
    {
        var response = await AdminClient.GetAsync("api/catalog");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await DataAsync(response);
    }

    private async Task<IReadOnlyList<WorkspaceResponse>> WorkspacesAsync() =>
        await (await AdminClient.GetAsync("api/workspaces")).ReadEnvelopeAsync<IReadOnlyList<WorkspaceResponse>>();

    private async Task CreateProductAsync(string slug, string repository)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/products")
        {
            Content = JsonContent.Create(new { slug, name = slug, repository }),
            Headers = { { "X-Wheelhouse-Action", "product" } },
        };
        (await AdminClient.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<JsonElement> ProductAsync(string slug) => await DataAsync(await AdminClient.GetAsync("api/products/" + slug));

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    private static string[] Slugs(JsonElement catalog, string list) =>
        [.. catalog.GetProperty(list).EnumerateArray().Select(item => item.GetProperty("slug").GetString()!)];

    private static string? Error(JsonElement catalog, int source) => catalog.GetProperty("sources")[source].GetProperty("error").GetString();

    [Fact]
    public async Task Get_ShouldMergeEveryHubInSettingsOrder_TaggingWorkspacesAndEntriesWithTheirHub()
    {
        var commits = PushHubs();

        var response = await AdminClient.GetAsync("api/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        var catalog = await DataAsync(response);
        catalog.GetProperty("sources").EnumerateArray()
            .Select(source => (source.GetProperty("repository").GetString(), source.GetProperty("path").GetString(),
                source.GetProperty("ref").GetString(), source.GetProperty("name").GetString(), source.GetProperty("commit").GetString(),
                source.GetProperty("error").ValueKind))
            .Should().Equal(
                (Wow2, "catalog.yml", "main", "wow2", commits.Wow2, JsonValueKind.Null),
                (Ventures, "catalog.yml", "main", "10x Ventures", commits.Ventures, JsonValueKind.Null),
                (Ocharo, "catalog.yml", "main", "Ocharo", commits.Ocharo, JsonValueKind.Null));
        catalog.GetProperty("workspaces").EnumerateArray()
            .Select(workspace => (workspace.GetProperty("slug").GetString(), workspace.GetProperty("source").GetString()))
            .Should().Equal(("personal", Wow2), ("wow2-platform", Wow2), ("own-ventures", Ventures), ("other-ventures", Ventures),
                ("ocharo", Ocharo));
        catalog.GetProperty("entries").EnumerateArray()
            .Select(entry => (entry.GetProperty("slug").GetString(), entry.GetProperty("source").GetString()))
            .Should().Equal(("wheelhouse", Wow2), ("secrets-vault", Wow2), ("wheelhousecanary", Wow2), ("notes-app", Wow2),
                ("pipelines", Wow2), ("foreverpin", Ventures), ("haven", Ventures), ("ocharo-platform", Ocharo));
    }

    [Fact]
    public async Task Get_ShouldListEntriesWithTheirProducts_IgnoringRegistryOnlyKeys()
    {
        PushHubs();

        var catalog = await CatalogAsync();

        catalog.GetProperty("workspaces")[0].GetProperty("description").GetString().Should().Be("Personal tools and documents outside the ecosystem");
        var entries = catalog.GetProperty("entries").EnumerateArray().ToList();
        var vault = entries[1];
        (vault.GetProperty("name").GetString(), vault.GetProperty("workspace").GetString(), vault.GetProperty("kind").GetString(),
                vault.GetProperty("deploy").GetString(), vault.GetProperty("purpose").GetString())
            .Should().Be(("Secrets Vault", "wow2-platform", "platform", "wheelhouse", "Secrets vault — envelope-encrypted store + management UI"));
        Strings(entries[2].GetProperty("repositories")).Should().Equal("wow-two-platform/wow-two-platform.wheelhouse-canary");
        Strings(entries[3].GetProperty("repositories")).Should().BeEmpty();
        vault.TryGetProperty("status", out _).Should().BeFalse();
        vault.TryGetProperty("local", out _).Should().BeFalse();
        catalog.TryGetProperty("hubs", out _).Should().BeFalse();

        entries[5].GetProperty("productId").GetString().Should().Be((await ProductAsync("foreverpin")).GetProperty("id").GetString());
        entries[0].GetProperty("productId").GetString().Should().Be((await ProductAsync("wheelhouse")).GetProperty("id").GetString());
        entries[1].GetProperty("productId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_ShouldNameWhatAnImportWouldBringInStep_ForEachExistingProduct()
    {
        PushHubs();

        var changes = (await CatalogAsync()).GetProperty("entries").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("slug").GetString()!, entry => Strings(entry.GetProperty("changes")));

        changes["wheelhouse"].Should().Equal(["workspace"], "the seed keeps it in own-ventures, and its entry says CI deploys it");
        changes["foreverpin"].Should().BeEmpty("it is in its entry's workspace, deployed by Wheelhouse, with a release source");
        changes["secrets-vault"].Should().BeEmpty("it is no product yet");

        PushCatalog(Catalog.Replace("deploy: ci", "deploy: wheelhouse"));
        var turned = (await CatalogAsync()).GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("slug").GetString() == "wheelhouse");
        Strings(turned.GetProperty("changes")).Should().Equal("workspace", "deploy", "release");
    }

    [Fact]
    public async Task Get_ShouldReadEachHubsNewestCommit_WhenACatalogChanges()
    {
        var first = PushHubs();

        var newer = PushCatalog(VenturesCatalog.Replace("name: Own ventures", "name: Mine"), Ventures);
        var catalog = await CatalogAsync();

        catalog.GetProperty("sources")[1].GetProperty("commit").GetString().Should().Be(newer).And.NotBe(first.Ventures);
        catalog.GetProperty("sources")[0].GetProperty("commit").GetString().Should().Be(first.Wow2);
        catalog.GetProperty("workspaces")[2].GetProperty("name").GetString().Should().Be("Mine");
    }

    [Fact]
    public async Task Get_ShouldShowEachHubsError_WhenGitHubCannotServeThem()
    {
        var catalog = await CatalogAsync();

        catalog.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("commit").ValueKind)
            .Should().AllBeEquivalentTo(JsonValueKind.Null);
        Error(catalog, 0).Should().Contain("wow-two/wow-two-ws@main could not be read from GitHub");
        Error(catalog, 1).Should().Contain("sulton-max/10x-ventures@main could not be read from GitHub");
        Error(catalog, 2).Should().Contain("ocharo-hq/ocharo-ws@main could not be read from GitHub");
        catalog.GetProperty("workspaces").GetArrayLength().Should().Be(0);
        catalog.GetProperty("entries").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Get_ShouldListTheOtherHubs_WhenOneHubFails()
    {
        PushHubs(ventures: "catalog: 1\nworkspaces: [\n");

        var catalog = await CatalogAsync();

        (Error(catalog, 0), Error(catalog, 2)).Should().Be((null, null));
        Error(catalog, 1).Should().StartWith("catalog.yml in sulton-max/10x-ventures@main, line 3");
        catalog.GetProperty("sources")[1].GetProperty("commit").GetString().Should().NotBeNull();
        Slugs(catalog, "entries").Should().Equal("wheelhouse", "secrets-vault", "wheelhousecanary", "notes-app", "pipelines", "ocharo-platform");
        Slugs(catalog, "workspaces").Should().Equal("personal", "wow2-platform", "ocharo");
    }

    [Fact]
    public async Task Get_ShouldShowTheHubsError_WhenItHasNoCatalog_OrItCannotBeRead()
    {
        PushHubs();
        Fixture.RepositoryFiles.PushEmpty(Ventures);
        Fixture.RepositoryFiles.Push(Ocharo, "catalog.yml", RepositoryFileStatus.Unreadable);

        var catalog = await CatalogAsync();

        Error(catalog, 1).Should().Be("sulton-max/10x-ventures@main has no catalog.yml.");
        Error(catalog, 2).Should().Be("catalog.yml in ocharo-hq/ocharo-ws@main could not be read from GitHub; try again.");
        Slugs(catalog, "entries").Should().HaveCount(5);
    }

    [Fact]
    public async Task Get_ShouldFailTheLaterHubNamingBoth_WhenTwoHubsListTheSameEntry()
    {
        PushHubs(ventures: VenturesCatalog.Replace("- slug: haven", "- slug: secrets-vault"));

        var catalog = await CatalogAsync();

        Error(catalog, 1).Should().Be(
            "catalog.yml in sulton-max/10x-ventures@main lists the entry 'secrets-vault', which catalog.yml in "
            + "wow-two/wow-two-ws@main lists too; each entry belongs to one hub.");
        (Error(catalog, 0), Error(catalog, 2)).Should().Be((null, null));
        Slugs(catalog, "entries").Should().NotContain("foreverpin").And.ContainSingle(slug => slug == "secrets-vault")
            .And.Contain("ocharo-platform");
        catalog.GetProperty("entries")[1].GetProperty("source").GetString().Should().Be(Wow2);
        Slugs(catalog, "workspaces").Should().Equal("personal", "wow2-platform", "ocharo");
    }

    [Fact]
    public async Task Get_ShouldFailTheLaterHubNamingBoth_WhenTwoHubsDeclareTheSameWorkspace()
    {
        PushHubs(ocharo: OcharoCatalog.Replace("- slug: ocharo\n", "- slug: own-ventures\n").Replace("workspace: ocharo", "workspace: own-ventures"));

        var catalog = await CatalogAsync();

        Error(catalog, 2).Should().Be(
            "catalog.yml in ocharo-hq/ocharo-ws@main declares the workspace 'own-ventures', which catalog.yml in "
            + "sulton-max/10x-ventures@main declares too; each workspace belongs to one hub.");
        Slugs(catalog, "entries").Should().NotContain("ocharo-platform");
    }

    [Theory]
    [InlineData("catalog: 1\nworkspaces: [\n", "line 3")]
    [InlineData("catalog: 2\nworkspaces: []\nentries: []\n", "line 1: catalog must be 1")]
    [InlineData("catalog: 1\nentries: []\n", "workspaces is required")]
    [InlineData("catalog: 1\nhub: [wow2]\nworkspaces: []\nentries: []\n", "line 2: hub must be a mapping")]
    public async Task Get_ShouldShowTheErrorNamingTheLine_WhenACatalogIsMalformed(string text, string expected)
    {
        PushCatalog(text);

        var catalog = await CatalogAsync();

        Error(catalog, 0).Should().StartWith("catalog.yml in wow-two/wow-two-ws@main, ").And.Contain(expected);
        catalog.GetProperty("sources")[0].GetProperty("name").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("workspace: personal\n    kind: app", "workspace: ghost\n    kind: app", "line 56: entries[3].workspace names 'ghost'")]
    [InlineData("kind: platform\n    deploy: wheelhouse\n    status: \"Active\"",
        "kind: platform\n    deploy: sometimes\n    status: \"Active\"", "line 42: entries[1].deploy must be wheelhouse, ci or none")]
    [InlineData("- slug: pipelines", "- slug: notes-app", "line 63: entries[4].slug repeats the entry 'notes-app'")]
    public async Task Get_ShouldShowTheErrorNamingTheLineAndField_WhenAnEntryBreaksARule(string find, string replace, string expected)
    {
        var text = Catalog.Replace(find, replace);
        text.Should().NotBe(Catalog);
        PushCatalog(text);

        Error(await CatalogAsync(), 0).Should().Contain(expected);
    }

    [Fact]
    public async Task Endpoints_ShouldTakeTheOwnersSessionOnly()
    {
        PushHubs();
        var created = await AdminClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, "api/integration-keys")
        {
            Content = JsonContent.Create(new { name = "Reader", scopes = new[] { "catalog:read" } }),
            Headers = { { "X-Wheelhouse-Action", "key-create" } },
        });
        var secret = (await DataAsync(created)).GetProperty("secret").GetString();
        var key = AnonymousClient;
        key.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        (await AnonymousClient.GetAsync("api/catalog")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await key.GetAsync("api/catalog")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AdminClient.PostJsonAsync("api/catalog/import", new { slugs = new[] { "secrets-vault" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the import carries the catalog action");
    }

    [Fact]
    public async Task Import_ShouldCreateWorkspacesAndProductsAndMoveExistingOnes()
    {
        PushHubs();
        Fixture.RepositoryFiles.Push("wow-two-platform/wow-two-platform.wheelhouse-canary", "engineering/deployment/deploy.yml", CanaryDescriptor);
        Fixture.RepositoryFiles.PushEmpty("wow-two-platform/wow-two-platform.secrets-vault");

        var response = await ImportClient().PostJsonAsync("api/catalog/import",
            new { slugs = new[] { "foreverpin", "wheelhousecanary", "secrets-vault", "wheelhouse", "notes-app", "pipelines" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await DataAsync(response);
        Strings(result.GetProperty("createdWorkspaces")).Should().Equal("wow2-platform");
        Strings(result.GetProperty("createdProducts")).Should().Equal("secrets-vault", "wheelhousecanary");
        Strings(result.GetProperty("movedProducts")).Should().Equal("wheelhouse");
        var warnings = result.GetProperty("warnings").EnumerateArray()
            .Select(warning => (warning.GetProperty("slug").GetString(), warning.GetProperty("message").GetString()!)).ToList();
        warnings.Select(warning => warning.Item1).Should().Equal("secrets-vault", "notes-app", "pipelines");
        warnings[0].Item2.Should().Be(
            "wow-two-platform/wow-two-platform.secrets-vault@main has no engineering/deployment/deploy.yml, so the product has no images yet.");
        warnings[1].Item2.Should().Contain("no GitHub repository");
        warnings[2].Item2.Should().Contain("deploy: none");

        var workspaces = await WorkspacesAsync();
        workspaces.Select(workspace => (workspace.Slug, workspace.Order)).Should().Equal(("own-ventures", 0), ("wow2-platform", 1));
        var platform = workspaces[1];
        (platform.Name, platform.Description).Should().Be(("wow2 platform", "Platform apps that deploy and run the portfolio"));

        var canary = await ProductAsync("wheelhousecanary");
        (canary.GetProperty("name").GetString(), canary.GetProperty("description").GetString(), canary.GetProperty("workspaceId").GetString(),
                canary.GetProperty("status").GetString())
            .Should().Be(("Wheelhouse Canary", "Sample app Wheelhouse deploys to prove every flow", platform.Id.ToString(), "active"));
        var repository = canary.GetProperty("repository");
        (repository.GetProperty("name").GetString(), repository.GetProperty("defaultBranch").GetString())
            .Should().Be(("wow-two-platform/wow-two-platform.wheelhouse-canary", "main"));
        var release = canary.GetProperty("release");
        (release.GetProperty("asset").GetString(), release.GetProperty("workflow").GetString())
            .Should().Be(("wheelhousecanary-release.tar.gz", "publish-docker-image.yml"));
        release.GetProperty("images").EnumerateArray()
            .Select(image => (image.GetProperty("service").GetString(), image.GetProperty("image").GetString()))
            .Should().Equal(
                ("api", "ghcr.io/wow-two-platform/wow-two-platform.wheelhouse-canary/api"),
                ("web", "ghcr.io/wow-two-platform/canary-web"));

        var vault = await ProductAsync("secrets-vault");
        vault.GetProperty("release").GetProperty("asset").GetString().Should().Be("secrets-vault-release.tar.gz");
        vault.GetProperty("release").GetProperty("images").GetArrayLength().Should().Be(0);
        (await ProductAsync("wheelhouse")).GetProperty("workspaceId").GetString().Should().Be(platform.Id.ToString());
        (await ProductAsync("foreverpin")).GetProperty("workspaceId").GetString().Should().Be(workspaces[0].Id.ToString());
        (await AdminClient.GetAsync("api/products/notes-app")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AdminClient.GetAsync("api/products/pipelines")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The created products reach the runner's snapshot with their release sources.
        var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture.InventoryRoot, "inventory.json"))).RootElement;
        snapshot.GetProperty("products").EnumerateArray().Select(product => product.GetProperty("slug").GetString())
            .Should().Contain(["secrets-vault", "wheelhousecanary"]);
    }

    [Fact]
    public async Task Import_ShouldTakeEntriesFromEveryHub_CreatingEachHubsWorkspaces()
    {
        PushHubs();

        var result = await DataAsync(await ImportClient().PostJsonAsync("api/catalog/import",
            new { slugs = new[] { "ocharo-platform", "haven", "secrets-vault" } }));

        Strings(result.GetProperty("createdWorkspaces")).Should().Equal("wow2-platform", "other-ventures", "ocharo");
        Strings(result.GetProperty("createdProducts")).Should().Equal("secrets-vault", "haven", "ocharo-platform");
        var workspaces = (await WorkspacesAsync()).ToDictionary(workspace => workspace.Slug, workspace => workspace.Id.ToString());
        (await ProductAsync("haven")).GetProperty("workspaceId").GetString().Should().Be(workspaces["other-ventures"]);
        var ocharo = await ProductAsync("ocharo-platform");
        (ocharo.GetProperty("workspaceId").GetString(), ocharo.GetProperty("repository").GetProperty("name").GetString())
            .Should().Be((workspaces["ocharo"], "ocharo-hq/ocharo-platform"));
        (await CatalogAsync()).GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("slug").GetString() == "ocharo-platform")
            .GetProperty("productId").GetString().Should().Be(ocharo.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Import_ShouldTakeTheReadableHubsEntries_WhenAnotherHubFails()
    {
        PushHubs(ventures: "catalog: 1\nentries: []\n");

        var response = await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "ocharo-platform" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Strings((await DataAsync(response)).GetProperty("createdProducts")).Should().Equal("ocharo-platform");
    }

    [Fact]
    public async Task Import_ShouldSetEachExistingProductsDeployMode_FromItsEntry()
    {
        PushHubs();
        Fixture.RepositoryFiles.Push("wow-two-platform/wow-two-platform.wheelhouse-canary", "engineering/deployment/deploy.yml", CanaryDescriptor);
        await ProductDeployModes.SetAsync(AdminClient, "wheelhouse", "wheelhouse");
        await CreateProductAsync("pipelines", "wow-two-platform/wow-two-platform.pipelines");

        var result = await DataAsync(await ImportClient().PostJsonAsync("api/catalog/import",
            new { slugs = new[] { "wheelhouse", "pipelines", "wheelhousecanary" } }));

        Strings(result.GetProperty("updatedProducts")).Should().Equal("wheelhouse", "pipelines");
        Strings(result.GetProperty("createdProducts")).Should().Equal("wheelhousecanary");
        (await ProductAsync("wheelhouse")).GetProperty("deploy").GetString().Should().Be("ci", "its entry says deploy: ci");
        (await ProductAsync("pipelines")).GetProperty("deploy").GetString().Should().Be("none", "its entry says deploy: none");
        (await ProductAsync("wheelhousecanary")).GetProperty("deploy").GetString().Should().Be("wheelhouse");
        (await DataAsync(await AdminClient.GetAsync("/api/deployments/automation"))).GetProperty("products").EnumerateArray()
            .Single(item => item.GetProperty("product").GetString() == "wheelhouse").GetProperty("ciDeployed").GetBoolean()
            .Should().BeTrue();

        var again = await DataAsync(await ImportClient().PostJsonAsync("api/catalog/import",
            new { slugs = new[] { "wheelhouse", "pipelines", "wheelhousecanary" } }));
        Strings(again.GetProperty("updatedProducts")).Should().BeEmpty();
        Strings(again.GetProperty("movedProducts")).Should().BeEmpty("pipelines is in its entry's workspace since the first run");
    }

    [Fact]
    public async Task Import_ShouldBringAnExistingProductInStep_WhenItsEntryTurnsToWheelhouse()
    {
        PushHubs();
        PushCatalog(Catalog.Replace("deploy: ci", "deploy: wheelhouse"));
        Fixture.RepositoryFiles.Push("wow-two-platform/wow-two-platform.wheelhouse", "engineering/deployment/deploy.yml",
            "descriptor: 1\nproduct: wheelhouse\nservices:\n  console:\n    build: { dockerfile: deployment/Dockerfile }\n    health: /api/system/ready\n");

        var result = await DataAsync(await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "wheelhouse" } }));

        Strings(result.GetProperty("updatedProducts")).Should().Equal("wheelhouse");
        Strings(result.GetProperty("movedProducts")).Should().Equal("wheelhouse");
        Strings(result.GetProperty("createdProducts")).Should().BeEmpty();
        result.GetProperty("warnings").GetArrayLength().Should().Be(0);
        var platform = (await WorkspacesAsync()).Single(workspace => workspace.Slug == "wow2-platform");
        var product = await ProductAsync("wheelhouse");
        (product.GetProperty("deploy").GetString(), product.GetProperty("workspaceId").GetString())
            .Should().Be(("wheelhouse", platform.Id.ToString()));
        (product.GetProperty("name").GetString(), product.GetProperty("description").GetString())
            .Should().Be(("Wheelhouse", "The portfolio's control plane."), "the catalog does not own an existing product's name");
        var release = product.GetProperty("release");
        (release.GetProperty("asset").GetString(), release.GetProperty("workflow").GetString())
            .Should().Be(("wheelhouse-release.tar.gz", "publish-docker-image.yml"));
        release.GetProperty("images").EnumerateArray()
            .Select(image => (image.GetProperty("service").GetString(), image.GetProperty("image").GetString()))
            .Should().Equal(("console", "ghcr.io/wow-two-platform/wow-two-platform.wheelhouse/console"));
        var entry = (await CatalogAsync()).GetProperty("entries").EnumerateArray()
            .Single(item => item.GetProperty("slug").GetString() == "wheelhouse");
        Strings(entry.GetProperty("changes")).Should().BeEmpty();

        var again = await DataAsync(await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "wheelhouse" } }));
        Strings(again.GetProperty("updatedProducts")).Should().BeEmpty();
        Strings(again.GetProperty("movedProducts")).Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ShouldChangeNothingMore_WhenRunAgain()
    {
        PushHubs();
        var slugs = new { slugs = new[] { "secrets-vault", "wheelhouse", "foreverpin" } };
        (await ImportClient().PostJsonAsync("api/catalog/import", slugs)).StatusCode.Should().Be(HttpStatusCode.OK);
        var products = (await AdminClient.GetStringAsync("api/products")).Length;

        var again = await DataAsync(await ImportClient().PostJsonAsync("api/catalog/import", slugs));

        Strings(again.GetProperty("createdWorkspaces")).Should().BeEmpty();
        Strings(again.GetProperty("createdProducts")).Should().BeEmpty();
        Strings(again.GetProperty("movedProducts")).Should().BeEmpty();
        // The first run could not read secrets-vault's repository, so it was created without images and said so.
        again.GetProperty("warnings").GetArrayLength().Should().Be(0);
        (await WorkspacesAsync()).Should().HaveCount(2);
        (await AdminClient.GetStringAsync("api/products")).Length.Should().Be(products);
    }

    [Fact]
    public async Task Import_ShouldCreateAnEntrysWorkspaceAfterTheOwnersOwn()
    {
        PushHubs();
        var created = await AdminClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, "api/workspaces")
        {
            Content = JsonContent.Create(new { slug = "scratch", name = "Scratch" }),
            Headers = { { "X-Wheelhouse-Action", "workspace" } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        (await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "secrets-vault" } })).EnsureSuccessStatusCode();

        (await WorkspacesAsync()).Select(workspace => (workspace.Slug, workspace.Order))
            .Should().Equal(("own-ventures", 0), ("scratch", 1), ("wow2-platform", 2));
    }

    [Fact]
    public async Task Import_ShouldNoteADescriptorThatNamesAnotherProduct_OrCannotBeParsed()
    {
        PushHubs();
        Fixture.RepositoryFiles.Push("wow-two-platform/wow-two-platform.wheelhouse-canary", "engineering/deployment/deploy.yml",
            CanaryDescriptor.Replace("product: wheelhousecanary", "product: canary"));
        Fixture.RepositoryFiles.Push("wow-two-platform/wow-two-platform.secrets-vault", "engineering/deployment/deploy.yml",
            "descriptor: 1\nproduct: secrets-vault\nservices: [api]\n");

        var result = await DataAsync(await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "wheelhousecanary", "secrets-vault" } }));

        var warnings = result.GetProperty("warnings").EnumerateArray().ToDictionary(
            warning => warning.GetProperty("slug").GetString()!, warning => warning.GetProperty("message").GetString()!);
        warnings["wheelhousecanary"].Should().Contain("names the product 'canary'");
        warnings["secrets-vault"].Should().Contain("line 3: services must be a mapping").And.Contain("no images");
        (await ProductAsync("wheelhousecanary")).GetProperty("release").GetProperty("images").GetArrayLength().Should().Be(2);
        (await ProductAsync("secrets-vault")).GetProperty("release").GetProperty("images").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Import_ShouldReturn400AndChangeNothing_WhenAnEntryIsUnknown()
    {
        PushHubs();

        var response = await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "secrets-vault", "ghost" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Detail.Should().Be("The catalog has no entry 'ghost'.");
        (await AdminClient.GetAsync("api/products/secrets-vault")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await WorkspacesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Import_ShouldReturn400NamingTheHubsNotListed_WhenAnEntrysHubCannotBeRead()
    {
        PushCatalog(Catalog.Replace("kind: backbone", "kind: startup"));

        var response = await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "secrets-vault", "haven" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Detail.Should().StartWith("The catalog has no entry 'secrets-vault', 'haven'. Hubs not listed: ")
            .And.Contain("entries[4].kind must be core, meta, platform")
            .And.Contain("sulton-max/10x-ventures@main could not be read from GitHub")
            .And.Contain("ocharo-hq/ocharo-ws@main could not be read from GitHub");
        (await WorkspacesAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not_A_Slug")]
    public async Task Import_ShouldReturn400_WhenTheSlugsAreMissingOrMalformed(string slug)
    {
        PushHubs();
        string[] slugs = slug.Length == 0 ? [] : [slug];

        (await ImportClient().PostJsonAsync("api/catalog/import", new { slugs })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_ShouldLandInTheAuditTrail()
    {
        PushHubs();

        (await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "secrets-vault", "wheelhouse" } })).EnsureSuccessStatusCode();

        var entry = (await AdminClient.AuditItemsAsync()).EnumerateArray().Single();
        (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(), entry.GetProperty("outcome").GetString(),
                entry.GetProperty("detail").GetString())
            .Should().Be(("catalog.import", "catalog", "succeeded", "secrets-vault, wheelhouse"));
    }
}
