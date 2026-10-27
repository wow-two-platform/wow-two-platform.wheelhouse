using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.Products;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="DescriptorIdentityParser"/>: the identity a <c>deploy.yml</c> declares reads whole, a
/// descriptor without identity keys declares none, and a broken identity is refused with the line and the field, never
/// the value.</summary>
public sealed class DescriptorIdentityParserTests
{
    private const string Services = """
        services:
          edge:
            name: Site
            build: { dockerfile: deployment/edge.Dockerfile }
            paths: [codebase/**]
            health: /healthz
          auth:
            name: Accounts
            description: Sign-in and accounts.
            build: { dockerfile: deployment/backend.Dockerfile }
            paths: [codebase/**]
            health: /health
          worker:
            build: { dockerfile: deployment/backend.Dockerfile }
            paths: [codebase/**]
            health: /health

        """;

    private static Wheelhouse.Application.Products.Models.ProductManifestParse Parse(string text) =>
        DescriptorIdentityParser.Parse(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Parse_ShouldReadTheWholeIdentity_WhenTheDescriptorDeclaresOne()
    {
        var result = Parse("""
            # yaml-language-server: $schema=../../../../../conventions/deployment/descriptor/deploy.schema.json
            descriptor: 2
            product: haven
            name: Haven
            description: "Listings, leads and clients: for a realtor."
            homepage: https://haven.example/home?ref=docs

            """ + Services);

        result.Problem.Should().BeNull();
        var manifest = result.Manifest!;
        (manifest.Name, manifest.Description, manifest.Homepage).Should().Be(
            ("Haven", "Listings, leads and clients: for a realtor.", "https://haven.example/home?ref=docs"));
        manifest.Services.Select(service => (service.Service, service.Name, service.Description)).Should().Equal(
            ("auth", "Accounts", "Sign-in and accounts."), ("edge", "Site", null));
    }

    [Fact]
    public void Parse_ShouldReadAMinimalIdentity_WhenOnlyNameAndDescriptionAreGiven()
    {
        var manifest = Parse("descriptor: 2\nproduct: pilot\nname: Pilot\ndescription: A pilot product.\n" +
            Services.Replace("    name: Site\n", "").Replace("    name: Accounts\n    description: Sign-in and accounts.\n", "")).Manifest!;

        (manifest.Name, manifest.Description, manifest.Homepage).Should().Be(("Pilot", "A pilot product.", null));
        manifest.Services.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShouldDeclareNone_WhenTheDescriptorHasNoIdentityKeys()
    {
        var result = Parse("descriptor: 2\nproduct: pilot\nname:\n" +
            Services.Replace("    name: Site\n", "").Replace("    name: Accounts\n    description: Sign-in and accounts.\n", ""));

        result.IsAbsent.Should().BeTrue("an empty name is no name");
    }

    [Fact]
    public void Parse_ShouldReadAFoldedDescription_WhenItFoldsToOneLine()
    {
        var manifest = Parse("descriptor: 2\nproduct: pilot\nname: Pilot\ndescription: >-\n  A pilot\n  product.\n" + Services).Manifest!;

        manifest.Description.Should().Be("A pilot product.");
    }

    [Theory]
    [InlineData("name: Pilot\n", "line 1: description is required beside name.")]
    [InlineData("description: x\n", "line 1: name is required beside description.")]
    [InlineData("homepage: https://pilot.example\n", "line 1: name is required beside description.")]
    [InlineData("name: \"\"\ndescription: x\n", "line 3: name must hold 1–80 characters.")]
    [InlineData("name: \" Pilot\"\ndescription: x\n", "line 3: name must be one line without leading or trailing space.")]
    [InlineData("name: Pilot\ndescription: \"two\\nlines\"\n", "line 4: description must be one line without leading or trailing space.")]
    [InlineData("name: { first: Pilot }\ndescription: x\n", "line 3: name must be text.")]
    [InlineData("name: Pilot\ndescription: x\nhomepage: https://user:secret@pilot.example\n",
        "line 5: homepage must be an http or https address without credentials, at most 300 characters.")]
    public void Parse_ShouldRefuseWithTheLineAndField_WhenTheIdentityBreaksARule(string identity, string problem)
    {
        var result = Parse("descriptor: 2\nproduct: pilot\n" + identity + Services);

        (result.Manifest, result.Problem).Should().Be((null, problem));
        result.Problem.Should().NotContain("secret");
    }

    [Fact]
    public void Parse_ShouldRefuseAServiceText_WhenItBreaksARule()
    {
        var result = Parse("descriptor: 2\nproduct: pilot\nname: Pilot\ndescription: x\n" +
            Services.Replace("    name: Site\n", "    name: \"" + new string('s', 81) + "\"\n"));

        result.Problem.Should().Be("line 7: services.edge.name must hold 1–80 characters.");
    }

    [Fact]
    public void Parse_ShouldRefuseWithTheLine_WhenTheFileIsNotYaml()
    {
        var result = Parse("descriptor: 2\nproduct: [pilot\n");

        result.Problem.Should().EndWith("deploy.yml is not valid YAML.").And.StartWith("line ");
    }

    [Fact]
    public void Parse_ShouldAcceptThisRepositorysOwnDescriptor_WhenItIsRead()
    {
        // Wheelhouse is a product like any other; its own identity must read by the rules it applies to the rest.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "engineering", "deployment", "deploy.yml")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the tests run inside the Wheelhouse repository");

        var result = DescriptorIdentityParser.Parse(
            File.ReadAllBytes(Path.Combine(directory!.FullName, "engineering", "deployment", "deploy.yml")));

        result.Problem.Should().BeNull();
        (result.Manifest!.Name, result.Manifest.Homepage).Should().Be(
            ("Wheelhouse", "https://github.com/wow-two-platform/wow-two-platform.wheelhouse"));
    }
}
