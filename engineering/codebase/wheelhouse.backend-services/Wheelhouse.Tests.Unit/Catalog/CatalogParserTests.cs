using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.Catalog;
using Wheelhouse.Application.Catalog.Parsers;
using Wheelhouse.Domain.Catalog.Enums;

namespace Wheelhouse.Tests.Unit.Catalog;

/// <summary>Tests for <see cref="CatalogParser"/>, <see cref="DeployDescriptorParser"/> and <see cref="CatalogSettings"/>.</summary>
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
        parsed.Value.Services.Should().Equal(("management", null), ("redirect", "ghcr.io/sulton-max/pin-redirect"));
    }

    [Theory]
    [InlineData("descriptor: 1\nservices: { api: {} }\n", "line 1: product is required.")]
    [InlineData("product: Pin\nservices: { api: {} }\n", "line 1: product must be a lowercase slug.")]
    [InlineData("product: pin\nservices: {}\n", "line 2: services must name at least one service.")]
    [InlineData("product: pin\nservices:\n  Api: {}\n", "line 3: services must be keyed by lowercase slugs.")]
    [InlineData("product: pin\nservices: [api]\n", "line 2: services must be a mapping.")]
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
        new CatalogSettings { Repository = repository, Path = path, Ref = reference }.IsValid.Should().Be(valid);
    }
}
