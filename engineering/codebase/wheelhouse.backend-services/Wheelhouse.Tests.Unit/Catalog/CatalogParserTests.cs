using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.Catalog;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Parsers;
using Wheelhouse.Domain.Catalog.Enums;

namespace Wheelhouse.Tests.Unit.Catalog;

/// <summary>Tests for <see cref="CatalogParser"/>, <see cref="DeployDescriptorParser"/>, <see cref="CatalogSettings"/> and
/// <see cref="CatalogSourceSettings"/>.</summary>
public sealed class CatalogParserTests
{
    private const string Minimal =
        """
        catalog: 1
        orgs: [{ slug: wow-two, url: "https://github.com/wow-two", description: "core", label: core, intro: ["x"] }]
        workspaces:
          - slug: wow2-platform
            name: wow2 platform
          - slug: own-ventures
            name: Own ventures
            description: "The owner's ventures"
        entries:
          - slug: secrets-vault
            name: Secrets Vault
            workspace: wow2-platform
            kind: platform
            deploy: wheelhouse
            status: "Active"
            purpose: "Secrets: envelope-encrypted, with a UI"
            repositories: [wow-two-platform/wow-two-platform.secrets-vault]
            local: workbench/wow-two-platform/wow-two-platform.secrets-vault
            future: { anything: [1, 2] }
          - slug: idea
            name: Idea
            workspace: own-ventures
            kind: venture
            deploy: none
            status: "Idea"
            purpose: "Not started"
            repositories: []
        """;

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Parse_ShouldReadWorkspacesAndEntries_IgnoringUnknownKeys()
    {
        var parsed = CatalogParser.Parse(Bytes(Minimal));

        parsed.Problem.Should().BeNull();
        var catalog = parsed.Value!;
        catalog.Workspaces.Select(workspace => (workspace.Slug, workspace.Name, workspace.Description))
            .Should().Equal(("wow2-platform", "wow2 platform", null), ("own-ventures", "Own ventures", "The owner's ventures"));
        var vault = catalog.Entries[0];
        (vault.Slug, vault.Name, vault.Workspace, vault.Kind, vault.Deploy, vault.Purpose)
            .Should().Be(("secrets-vault", "Secrets Vault", "wow2-platform", CatalogKind.Platform, CatalogDeploy.Wheelhouse,
                "Secrets: envelope-encrypted, with a UI"));
        vault.Repositories.Should().Equal("wow-two-platform/wow-two-platform.secrets-vault");
        catalog.Entries[1].Repositories.Should().BeEmpty();
        catalog.Entries[1].Deploy.Should().Be(CatalogDeploy.None);
    }

    [Theory]
    [InlineData("catalog: 1\nworkspaces: [\n", "line 3: catalog.yml is not valid YAML.")]
    [InlineData("- a\n- b\n", "line 1: the catalog must be a mapping.")]
    [InlineData("catalog: 1\nworkspaces: []\n", "line 1: entries is required.")]
    [InlineData("catalog: one\nworkspaces: []\nentries: []\n", "line 1: catalog must be 1.")]
    [InlineData("catalog: 1\nworkspaces:\n  - slug: Bad\n    name: B\nentries: []\n", "line 3: workspaces[0].slug must be a lowercase slug.")]
    [InlineData("catalog: 1\nworkspaces:\n  - slug: a\n    name: A\n  - slug: a\n    name: A\nentries: []\n", "line 5: workspaces[1].slug repeats the workspace 'a'.")]
    [InlineData("catalog: 1\nworkspaces:\n  - slug: a\nentries: []\n", "line 3: workspaces[0].name is required.")]
    [InlineData("catalog: 1\nworkspaces: {}\nentries: []\n", "line 2: workspaces must be a list.")]
    public void Parse_ShouldNameTheLineAndField_WhenTheFileBreaksARule(string text, string problem)
    {
        var parsed = CatalogParser.Parse(Bytes(text));

        parsed.Value.Should().BeNull();
        parsed.Problem.Should().Be(problem);
    }

    [Theory]
    [InlineData("    kind: platform", "    kind: library", "line 13: entries[0].kind must be core, meta, platform, backbone, sdk, kb, app, venture or template.")]
    [InlineData("    deploy: wheelhouse", "    deploy: manual", "line 14: entries[0].deploy must be wheelhouse, ci or none.")]
    [InlineData("    workspace: wow2-platform", "    workspace: elsewhere", "line 12: entries[0].workspace names 'elsewhere', which workspaces does not declare.")]
    [InlineData("    name: Secrets Vault", "    name: \"\"", "line 11: entries[0].name must be one line of text.")]
    [InlineData("[wow-two-platform/wow-two-platform.secrets-vault]", "[not a repository]", "line 17: entries[0].repositories[0] must be a GitHub repository as owner/name.")]
    [InlineData("  - slug: idea", "  - slug: secrets-vault", "line 20: entries[1].slug repeats the entry 'secrets-vault'.")]
    [InlineData("    name: Secrets Vault\n", "    name: Secrets Vault\n    name: twice\n", "line 12: catalog.yml is not valid YAML.")]
    public void Parse_ShouldNameTheEntryField_WhenAnEntryBreaksARule(string find, string replace, string problem)
    {
        var text = Minimal.Replace(find, replace);
        text.Should().NotBe(Minimal);

        var parsed = CatalogParser.Parse(Bytes(text));

        parsed.Problem.Should().Be(problem);
    }

    [Fact]
    public void ParseDescriptor_ShouldReadTheProductAndEachServicesImage()
    {
        var parsed = DeployDescriptorParser.Parse(Bytes(
            """
            descriptor: 1
            product: foreverpin
            platform: linux/amd64
            services:
              management:
                build: { dockerfile: deployment/Dockerfile }
                paths: [codebase/**]
              redirect:
                image: ghcr.io/sulton-max/pin-redirect
            """));

        parsed.Problem.Should().BeNull();
        parsed.Value!.Product.Should().Be("foreverpin");
        parsed.Value.Services.Select(service => (service.Service, service.Image))
            .Should().Equal(("management", null), ("redirect", "ghcr.io/sulton-max/pin-redirect"));
        parsed.Value.Sites.Should().BeEmpty();
        parsed.Value.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void ParseDescriptor_ShouldReadAPinnedImagesRepository_WithoutItsTag()
    {
        var parsed = DeployDescriptorParser.Parse(Bytes(
            """
            descriptor: 2
            product: foreverpin
            platform: linux/amd64
            services:
              identity:
                image: ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.identity/identity:0.3.0
              cache:
                image: localhost:15000/pilot-cache
            """));

        parsed.Problem.Should().BeNull();
        parsed.Value!.Services.Select(service => (service.Service, service.Image))
            .Should().Equal(("identity", "ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.identity/identity"),
                ("cache", "localhost:15000/pilot-cache"));
    }

    [Fact]
    public void ParseDescriptor_ShouldReadTheDeclaredExtensionsOnceEachInFileOrder()
    {
        var parsed = DeployDescriptorParser.Parse(Bytes(
            """
            descriptor: 2
            product: haven
            extensions: [postgres_fdw, "uuid-ossp", postgres_fdw, pg_trgm]
            services:
              api:
                needs: [postgres]
            """));
        var empty = DeployDescriptorParser.Parse(Bytes("product: haven\nextensions: []\nservices: { api: {} }\n"));
        var bare = DeployDescriptorParser.Parse(Bytes("product: haven\nextensions:\nservices: { api: {} }\n"));

        parsed.Problem.Should().BeNull();
        parsed.Value!.Extensions.Should().Equal("postgres_fdw", "uuid-ossp", "pg_trgm");
        empty.Value!.Extensions.Should().BeEmpty();
        bare.Value!.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void ParseDescriptor_ShouldReadEachServicesNeedsAndDescriptorOnesSitesByName()
    {
        var parsed = DeployDescriptorParser.Parse(Bytes(
            """
            descriptor: 1
            product: ocharo-platform
            services:
              jobs:
                needs: [postgres]
              desk:
                needs: [postgres, valkey]
                sites:
                  app: { exposure: public }
                  admin: { exposure: private }
              image-worker:
                sites:
                  admin: {}
                  media:
            """));

        parsed.Problem.Should().BeNull();
        parsed.Value!.Services.Select(service => (service.Service, string.Join(",", service.Needs)))
            .Should().Equal(("jobs", "postgres"), ("desk", "postgres,valkey"), ("image-worker", ""));
        // A site any service makes private is private; the first site of the first service by name is the front door.
        parsed.Value.Sites.Should().Equal(
            new DeployDescriptorSite("admin", "private", true),
            new DeployDescriptorSite("app", "public", false),
            new DeployDescriptorSite("media", "public", false));
    }

    [Fact]
    public void ParseDescriptor_ShouldReadDescriptorTwosSitesInFileOrder_WithTheMarkedOrFirstSiteAsTheFrontDoor()
    {
        const string services = "services:\n  edge:\n    sites: { crm: { paths: [/] } }\n";
        var marked = DeployDescriptorParser.Parse(Bytes(
            "descriptor: 2\nproduct: haven\nsites:\n  landing: {}\n  crm: { primary: true }\n  admin: { exposure: private }\n"
            + services));
        var unmarked = DeployDescriptorParser.Parse(Bytes(
            "descriptor: 2\nproduct: haven\nsites:\n  landing:\n  admin: { exposure: private }\n" + services));

        marked.Value!.Sites.Should().Equal(
            new DeployDescriptorSite("landing", "public", false),
            new DeployDescriptorSite("crm", "public", true),
            new DeployDescriptorSite("admin", "private", false));
        unmarked.Value!.Sites.Should().Equal(
            new DeployDescriptorSite("landing", "public", true), new DeployDescriptorSite("admin", "private", false));
    }

    [Theory]
    [InlineData("descriptor: 1\nservices: { api: {} }\n", "line 1: product is required.")]
    [InlineData("product: Pin\nservices: { api: {} }\n", "line 1: product must be a lowercase slug.")]
    [InlineData("product: pin\nservices: {}\n", "line 2: services must name at least one service.")]
    [InlineData("product: pin\nservices:\n  Api: {}\n", "line 3: services must be keyed by lowercase slugs.")]
    [InlineData("product: pin\nservices: [api]\n", "line 2: services must be a mapping.")]
    [InlineData("product: pin\nservices:\n  api: { needs: postgres }\n", "line 3: services.api.needs must be a list.")]
    [InlineData("product: pin\nservices:\n  api: { sites: { app: { exposure: open } } }\n", "line 3: services.api.sites.app.exposure must be public or private.")]
    [InlineData("descriptor: 2\nproduct: pin\nsites: { app: { primary: yes } }\nservices: { api: {} }\n", "line 3: sites.app.primary must be true or false.")]
    [InlineData("product: pin\nservices: { api: {} }\nextensions: uuid-ossp\n", "line 3: extensions must be a list.")]
    [InlineData("product: pin\nservices: { api: {} }\nextensions: [postgres_fdw, Uuid-Ossp]\n", "line 3: extensions must be lowercase extension names.")]
    [InlineData("product: pin\nservices: { api: {} }\nextensions:\n  - pgcrypto\n  - 'pg trgm'\n", "line 5: extensions must be lowercase extension names.")]
    [InlineData("product: pin\nservices: { api: {} }\nextensions: [1pg]\n", "line 3: extensions must be lowercase extension names.")]
    [InlineData("product: pin\nservices: { api: {} }\nextensions: [\"pgcrypto\\n\"]\n", "line 3: extensions must be lowercase extension names.")]
    [InlineData("product: pin\nservices: { api: {} }\nextensions: [{ name: pgcrypto }]\n", "line 3: extensions must be lowercase extension names.")]
    [InlineData("product: pin\nservices: { api: {} }\nextensions: [xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx]\n", "line 3: extensions must be lowercase extension names.")]
    public void ParseDescriptor_ShouldNameTheLineAndField_WhenTheFileBreaksARule(string text, string problem)
    {
        DeployDescriptorParser.Parse(Bytes(text)).Problem.Should().Be(problem);
    }

    [Fact]
    public void Image_ShouldFollowThePublishWorkflowsConvention()
    {
        CatalogConstants.Image("Sulton-Max/10x-venture-forever-pin", "management")
            .Should().Be("ghcr.io/sulton-max/10x-venture-forever-pin/management");
        CatalogConstants.Asset("foreverpin").Should().Be("foreverpin-release.tar.gz");
    }

    [Theory]
    [InlineData("wow-two/wow-two-ws", "catalog.yml", "main", true)]
    [InlineData("wow-two/wow-two-ws", "docs/catalog.yml", "release/v1", true)]
    [InlineData("wow-two", "catalog.yml", "main", false)]
    [InlineData("wow-two/wow-two-ws", "../catalog.yml", "main", false)]
    [InlineData("wow-two/wow-two-ws", "/catalog.yml", "main", false)]
    [InlineData("wow-two/wow-two-ws", "catalog.yml", "", false)]
    public void Settings_ShouldAcceptOnlyARepositoryARelativePathAndABranch(string repository, string path, string reference, bool valid)
    {
        new CatalogSourceSettings { Repository = repository, Path = path, Ref = reference }.IsValid.Should().Be(valid);
        new CatalogSettings { Sources = [new() { Repository = repository, Path = path, Ref = reference }] }.IsValid.Should().Be(valid);
    }

    [Fact]
    public void Settings_ShouldReadTheThreeHubs_WhenNoSourceIsConfigured()
    {
        var settings = new CatalogSettings();

        settings.Hubs.Select(hub => (hub.Repository, hub.Path, hub.Ref)).Should().Equal(
            ("wow-two/wow-two-ws", "catalog.yml", "main"),
            ("sulton-max/10x-ventures", "catalog.yml", "main"),
            ("ocharo-hq/ocharo-ws", "catalog.yml", "main"));
        settings.IsValid.Should().BeTrue();
        new CatalogSettings { Sources = [new() { Repository = "a/hub" }] }.Hubs.Should().ContainSingle();
    }

    [Fact]
    public void Settings_ShouldRefuseARepositoryListedTwice()
    {
        new CatalogSettings { Sources = [new() { Repository = "wow-two/wow-two-ws" }, new() { Repository = "Wow-Two/wow-two-ws", Path = "x.yml" }] }
            .IsValid.Should().BeFalse();
        new CatalogSettings { Sources = [.. Enumerable.Range(0, 21).Select(index => new CatalogSourceSettings { Repository = $"o/hub-{index}" })] }
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Parse_ShouldReadTheOptionalHub()
    {
        CatalogParser.Parse(Bytes(Minimal)).Value!.Hub.Should().BeNull();

        var hub = CatalogParser.Parse(Bytes(Minimal.Replace("catalog: 1\n", "catalog: 1\nhub:\n  slug: ventures\n  name: 10x Ventures\n  repository: sulton-max/10x-ventures\n")));

        hub.Problem.Should().BeNull();
        (hub.Value!.Hub!.Slug, hub.Value.Hub.Name).Should().Be(("ventures", "10x Ventures"));
        CatalogParser.Parse(Bytes(Minimal.Replace("catalog: 1\n", "catalog: 1\nhub:\n  slug: Ventures\n  name: V\n")))
            .Problem.Should().Be("line 3: hub.slug must be a lowercase slug.");
        CatalogParser.Parse(Bytes(Minimal.Replace("catalog: 1\n", "catalog: 1\nhub: [ventures]\n")))
            .Problem.Should().Be("line 2: hub must be a mapping.");
    }

    [Fact]
    public void Parse_ShouldReadAnEntrysRepositoriesInOrder_AndTheProductsItReplaces()
    {
        var text = Minimal.Replace(
            "repositories: [wow-two-platform/wow-two-platform.secrets-vault]",
            "repositories: [wow-two-platform/wow-two-platform.secrets-vault, wow-two-platform/vault-ui]\n    replaces: [vault, vault-ui]");

        var entry = CatalogParser.Parse(Bytes(text)).Value!.Entries[0];

        entry.Repositories.Should().Equal("wow-two-platform/wow-two-platform.secrets-vault", "wow-two-platform/vault-ui");
        entry.Replaces.Should().Equal("vault", "vault-ui");
        CatalogParser.Parse(Bytes(Minimal)).Value!.Entries[0].Replaces.Should().BeEmpty();
    }

    [Theory]
    [InlineData("replaces: [secrets-vault]", "names the entry itself or repeats a slug")]
    [InlineData("replaces: [vault, vault]", "names the entry itself or repeats a slug")]
    [InlineData("replaces: [Not_A_Slug]", "must be a lowercase slug")]
    public void Parse_ShouldRefuseAReplacesListThatNamesItselfRepeatsOrIsNoSlug(string replaces, string expected)
    {
        var text = Minimal.Replace(
            "repositories: [wow-two-platform/wow-two-platform.secrets-vault]",
            "repositories: [wow-two-platform/wow-two-platform.secrets-vault]\n    " + replaces);

        CatalogParser.Parse(Bytes(text)).Problem.Should().Contain("entries[0].replaces").And.Contain(expected);
    }
}
