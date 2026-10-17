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
/// E2E for the wow2 catalog — Wheelhouse reads <c>catalog.yml</c> from <c>wow-two/wow-two-ws@main</c> (the stub answers
/// for GitHub), lists its workspaces and entries with the product each already is, and imports entries: workspaces as
/// needed, a product per entry Wheelhouse deploys with images derived from its <c>deploy.yml</c>, and moves for existing
/// products. A malformed catalog is refused naming the line and the field.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class CatalogE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string CatalogRepository = "wow-two/wow-two-ws";

    /// <summary>A catalog shaped like the real one: registry-only keys, quoted free text, block workspaces and entries
    /// with and without repositories.</summary>
    private const string Catalog =
        """
        # yaml-language-server: $schema=conventions/development/repo/catalog/catalog.schema.json
        catalog: 1

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
          - slug: own-ventures
            name: Own ventures
            description: "The owner's product ventures"

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
            repositories: [wow-two-platform/wow-two-platform.wheelhouse-canary, wow-two-platform/canary-fixtures]
          - slug: foreverpin
            name: ForeverPin
            workspace: own-ventures
            kind: venture
            deploy: wheelhouse
            status: "Active, v0.9"
            purpose: "ForeverPin QR and programmable links"
            repositories: [sulton-max/10x-venture-forever-pin]
            local: workbench/ventures/10x-venture-forever-pin
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

    private string PushCatalog(string text = Catalog) => Fixture.RepositoryFiles.Push(CatalogRepository, "catalog.yml", text);

    private async Task<JsonElement> DataAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

    private async Task<IReadOnlyList<WorkspaceResponse>> WorkspacesAsync() =>
        await (await AdminClient.GetAsync("api/workspaces")).ReadEnvelopeAsync<IReadOnlyList<WorkspaceResponse>>();

    private async Task<JsonElement> ProductAsync(string slug) => await DataAsync(await AdminClient.GetAsync("api/products/" + slug));

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    [Fact]
    public async Task Get_ShouldListWorkspacesAndEntriesWithTheirProducts_IgnoringRegistryOnlyKeys()
    {
        var commit = PushCatalog();

        var response = await AdminClient.GetAsync("api/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        var catalog = await DataAsync(response);
        var source = catalog.GetProperty("source");
        (source.GetProperty("repository").GetString(), source.GetProperty("path").GetString(), source.GetProperty("ref").GetString(),
                source.GetProperty("commit").GetString())
            .Should().Be((CatalogRepository, "catalog.yml", "main", commit));
        catalog.GetProperty("workspaces").EnumerateArray().Select(workspace => workspace.GetProperty("slug").GetString())
            .Should().Equal("personal", "wow2-platform", "own-ventures");
        catalog.GetProperty("workspaces")[0].GetProperty("description").GetString().Should().Be("Personal tools and documents outside the ecosystem");

        var entries = catalog.GetProperty("entries").EnumerateArray().ToList();
        entries.Select(entry => entry.GetProperty("slug").GetString())
            .Should().Equal("wheelhouse", "secrets-vault", "wheelhousecanary", "foreverpin", "notes-app", "pipelines");
        var vault = entries[1];
        (vault.GetProperty("name").GetString(), vault.GetProperty("workspace").GetString(), vault.GetProperty("kind").GetString(),
                vault.GetProperty("deploy").GetString(), vault.GetProperty("purpose").GetString())
            .Should().Be(("Secrets Vault", "wow2-platform", "platform", "wheelhouse", "Secrets vault — envelope-encrypted store + management UI"));
        Strings(entries[2].GetProperty("repositories")).Should().Equal(
            "wow-two-platform/wow-two-platform.wheelhouse-canary", "wow-two-platform/canary-fixtures");
        Strings(entries[4].GetProperty("repositories")).Should().BeEmpty();
        vault.TryGetProperty("status", out _).Should().BeFalse();
        vault.TryGetProperty("local", out _).Should().BeFalse();

        var foreverPin = (await ProductAsync("foreverpin")).GetProperty("id").GetString();
        entries[3].GetProperty("productId").GetString().Should().Be(foreverPin);
        entries[0].GetProperty("productId").GetString().Should().Be((await ProductAsync("wheelhouse")).GetProperty("id").GetString());
        entries[1].GetProperty("productId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_ShouldReadTheNewestCommit_WhenTheCatalogChanges()
    {
        PushCatalog();
        var first = await DataAsync(await AdminClient.GetAsync("api/catalog"));

        var newer = PushCatalog(Catalog.Replace("name: Personal", "name: Mine"));
        var second = await DataAsync(await AdminClient.GetAsync("api/catalog"));

        second.GetProperty("source").GetProperty("commit").GetString().Should().Be(newer).And.NotBe(first.GetProperty("source").GetProperty("commit").GetString());
        second.GetProperty("workspaces")[0].GetProperty("name").GetString().Should().Be("Mine");
    }

    [Fact]
    public async Task Get_ShouldReturn503_WhenGitHubCannotServeTheCatalogRepository()
    {
        var response = await AdminClient.GetAsync("api/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.ReadProblemAsync()).Detail.Should().Contain("wow-two/wow-two-ws@main");
    }

    [Fact]
    public async Task Get_ShouldReturn404_WhenTheRepositoryHasNoCatalog()
    {
        Fixture.RepositoryFiles.PushEmpty(CatalogRepository);

        var response = await AdminClient.GetAsync("api/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Detail.Should().Contain("catalog.yml");
    }

    [Fact]
    public async Task Get_ShouldReturn503_WhenTheCatalogFileCannotBeRead()
    {
        Fixture.RepositoryFiles.Push(CatalogRepository, "catalog.yml", RepositoryFileStatus.Unreadable);

        (await AdminClient.GetAsync("api/catalog")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("catalog: 1\nworkspaces: [\n", "line 3")]
    [InlineData("catalog: 2\nworkspaces: []\nentries: []\n", "line 1: catalog must be 1")]
    [InlineData("catalog: 1\nentries: []\n", "workspaces is required")]
    public async Task Get_ShouldReturn422NamingTheLine_WhenTheCatalogIsMalformed(string text, string expected)
    {
        PushCatalog(text);

        var response = await AdminClient.GetAsync("api/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.ReadProblemAsync()).Detail.Should().StartWith("catalog.yml").And.Contain(expected);
    }

    [Theory]
    [InlineData("workspace: own-ventures\n    kind: venture\n    deploy: wheelhouse\n    status: \"Active, v0.9\"",
        "workspace: ghost\n    kind: venture\n    deploy: wheelhouse\n    status: \"Active, v0.9\"", "line 52: entries[3].workspace names 'ghost'")]
    [InlineData("kind: venture\n    deploy: wheelhouse\n    status: \"Active, v0.9\"",
        "kind: venture\n    deploy: sometimes\n    status: \"Active, v0.9\"", "line 54: entries[3].deploy must be wheelhouse, ci or none")]
    [InlineData("- slug: notes-app", "- slug: foreverpin", "line 59: entries[4].slug repeats the entry 'foreverpin'")]
    public async Task Get_ShouldReturn422NamingTheLineAndField_WhenAnEntryBreaksARule(string find, string replace, string expected)
    {
        PushCatalog(Catalog.Replace(find, replace));

        var response = await AdminClient.GetAsync("api/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Detail.Should().Contain(expected);
    }

    [Fact]
    public async Task Endpoints_ShouldTakeTheOperatorsSessionOnly()
    {
        PushCatalog();
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
        PushCatalog();
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
        warnings[0].Item2.Should().Contain("has no engineering/deployment/deploy.yml").And.Contain("no images");
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
    public async Task Import_ShouldChangeNothingMore_WhenRunAgain()
    {
        PushCatalog();
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
    public async Task Import_ShouldCreateAnEntrysWorkspaceAfterTheOperatorsOwn()
    {
        PushCatalog();
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
        PushCatalog();
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
        PushCatalog();

        var response = await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "secrets-vault", "ghost" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Detail.Should().Contain("'ghost'");
        (await AdminClient.GetAsync("api/products/secrets-vault")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await WorkspacesAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not_A_Slug")]
    public async Task Import_ShouldReturn400_WhenTheSlugsAreMissingOrMalformed(string slug)
    {
        PushCatalog();
        string[] slugs = slug.Length == 0 ? [] : [slug];

        (await ImportClient().PostJsonAsync("api/catalog/import", new { slugs })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_ShouldReturn422AndChangeNothing_WhenTheCatalogIsMalformed()
    {
        PushCatalog(Catalog.Replace("kind: venture\n    deploy: wheelhouse", "kind: startup\n    deploy: wheelhouse"));

        var response = await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "secrets-vault" } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Detail.Should().Contain("entries[3].kind must be core, meta, platform");
        (await WorkspacesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Import_ShouldLandInTheAuditTrail()
    {
        PushCatalog();

        (await ImportClient().PostJsonAsync("api/catalog/import", new { slugs = new[] { "secrets-vault", "wheelhouse" } })).EnsureSuccessStatusCode();

        var entry = (await DataAsync(await AdminClient.GetAsync("api/audit"))).EnumerateArray().Single();
        (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString(), entry.GetProperty("outcome").GetString(),
                entry.GetProperty("detail").GetString())
            .Should().Be(("catalog.import", "catalog", "succeeded", "secrets-vault, wheelhouse"));
    }
}
