using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.Products;

namespace Wheelhouse.Tests.Unit.Products;

/// <summary>Tests for <see cref="ProductManifestParser"/>: a valid manifest reads whole, and anything the product
/// manifest schema refuses is ignored with one reason that never quotes the file beyond a shortened key name.</summary>
public sealed class ProductManifestParserTests
{
    private const string Minimal = """{ "manifest": 1, "name": "Pilot", "description": "A pilot product." }""";

    private static string Manifest(string fields) => "{ \"manifest\": 1, \"name\": \"Pilot\", \"description\": \"A pilot.\"" + fields + " }";

    [Fact]
    public void Parse_ShouldReadTheWholeManifest_WhenItIsValid()
    {
        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes("""
            {
              "$schema": "../../../../conventions/design/identity/product.schema.json",
              "manifest": 1,
              "name": "Haven",
              "description": "Listings, leads and clients for a realtor.",
              "homepage": "https://haven.example/home?ref=docs",
              "services": {
                "edge": { "name": "Site" },
                "auth": { "name": "Accounts", "description": "Sign-in and accounts." }
              }
            }
            """));

        result.Problem.Should().BeNull();
        var manifest = result.Manifest!;
        (manifest.Name, manifest.Description, manifest.Homepage).Should().Be(
            ("Haven", "Listings, leads and clients for a realtor.", "https://haven.example/home?ref=docs"));
        manifest.Services.Select(service => (service.Service, service.Name, service.Description)).Should().Equal(
            ("auth", "Accounts", "Sign-in and accounts."), ("edge", "Site", null));
    }

    [Fact]
    public void Parse_ShouldReadAMinimalManifest_WhenOnlyTheRequiredKeysAreGiven()
    {
        var manifest = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Minimal)).Manifest!;

        (manifest.Name, manifest.Description, manifest.Homepage).Should().Be(("Pilot", "A pilot product.", null));
        manifest.Services.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShouldAcceptThisRepositorysOwnManifest_WhenItIsRead()
    {
        // Wheelhouse is a product like any other; its own manifest must read by the rules it applies to the rest.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "product", "product.json")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the tests run inside the Wheelhouse repository");

        var result = ProductManifestParser.Parse(File.ReadAllBytes(Path.Combine(directory!.FullName, "product", "product.json")));

        result.Problem.Should().BeNull();
        result.Manifest!.Name.Should().Be("Wheelhouse");
    }

    [Fact]
    public void Parse_ShouldIgnoreTheByteOrderMark_WhenTheFileStartsWithOne()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Minimal)).ToArray();

        ProductManifestParser.Parse(bytes).Manifest.Should().NotBeNull();
    }

    [Theory]
    [InlineData("""{ "manifest": 1, "name": "Pilot" }""", "'description' is required.")]
    [InlineData("""{ "manifest": 1, "description": "x" }""", "'name' is required.")]
    [InlineData("""{ "name": "Pilot", "description": "x" }""", "'manifest' is required.")]
    [InlineData("""{ "manifest": 2, "name": "Pilot", "description": "x" }""", "'manifest' must be 1.")]
    [InlineData("""{ "manifest": "1", "name": "Pilot", "description": "x" }""", "'manifest' must be 1.")]
    [InlineData("""{ "manifest": 1.5, "name": "Pilot", "description": "x" }""", "'manifest' must be 1.")]
    [InlineData("""{ "manifest": 1, "name": 7, "description": "x" }""", "'name' must be text.")]
    [InlineData("""{ "manifest": 1, "name": "Pilot", "description": null }""", "'description' must be text.")]
    [InlineData("""{ "manifest": 1, "name": "", "description": "x" }""", "'name' must hold 1–80 characters.")]
    [InlineData("""{ "manifest": 1, "name": "Pilot", "description": "x", "color": "red" }""", "'color' is not a manifest key.")]
    [InlineData("""{ "manifest": 1, "name": "Pilot", "description": "x", "$schema": 3 }""", "'$schema' must be text.")]
    [InlineData("""{ "manifest": 1, "name": "A", "name": "B", "description": "x" }""", "'name' is given twice.")]
    public void Parse_ShouldIgnoreTheFileWithAReason_WhenARequiredKeyIsMissingOrWrong(string json, string reason)
    {
        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        result.Manifest.Should().BeNull();
        result.Problem.Should().Be(reason);
    }

    [Theory]
    [InlineData(" Pilot")]
    [InlineData("Pilot ")]
    [InlineData("Pi\\nlot")]
    [InlineData("Pi\\tlot")]
    [InlineData("Pilot\\n")]
    [InlineData("   ")]
    public void Parse_ShouldIgnoreTheFile_WhenATextIsNotOneTrimmedLine(string name)
    {
        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(
            "{ \"manifest\": 1, \"name\": \"" + name + "\", \"description\": \"x\" }"));

        result.Manifest.Should().BeNull();
        result.Problem.Should().StartWith("'name' must");
    }

    [Fact]
    public void Parse_ShouldCountCharactersNotCodeUnits_WhenATextHoldsEmoji()
    {
        var name = string.Concat(Enumerable.Repeat("🚢", 80));
        var tooLong = name + "🚢";

        ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest("").Replace("Pilot", name))).Manifest.Should().NotBeNull();
        ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest("").Replace("Pilot", tooLong))).Problem
            .Should().Be("'name' must hold 1–80 characters.");
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(81, false)]
    public void Parse_ShouldBoundTheName_WhenItIsLong(int length, bool valid)
    {
        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest("").Replace("Pilot", new string('n', length))));

        (result.Manifest is not null).Should().Be(valid);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Parse_ShouldBoundTheDescription_WhenItIsLong(int length, bool valid)
    {
        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest("").Replace("A pilot.", new string('d', length))));

        (result.Manifest is not null).Should().Be(valid);
    }

    [Theory]
    [InlineData("https://pilot.example", true)]
    [InlineData("http://localhost:8080/path?x=1#top", true)]
    [InlineData("https://[::1]:8443/", true)]
    [InlineData("ftp://pilot.example", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("//pilot.example", false)]
    [InlineData("https://user:secret@pilot.example", false)]
    [InlineData("https://user@pilot.example", false)]
    [InlineData("https://pilot .example", false)]
    [InlineData("https://", false)]
    [InlineData("pilot.example", false)]
    [InlineData("https://pilot.example\\n", false)]
    public void Parse_ShouldAcceptOnlyAnHttpAddressWithoutCredentials_WhenAHomepageIsGiven(string homepage, bool valid)
    {
        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest($", \"homepage\": \"{homepage}\"")));

        (result.Manifest?.Homepage == homepage).Should().Be(valid);
        if (!valid)
            result.Problem.Should().StartWith("'homepage' must be");
    }

    [Fact]
    public void Parse_ShouldIgnoreTheFile_WhenTheHomepageIsTooLongOrNotText()
    {
        var tooLong = "https://pilot.example/" + new string('a', 300);

        ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest($", \"homepage\": \"{tooLong}\""))).Manifest.Should().BeNull();
        ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest(", \"homepage\": 5"))).Problem.Should().Be("'homepage' must be text.");
    }

    [Theory]
    [InlineData(", \"services\": []", "'services' must be an object.")]
    [InlineData(", \"services\": { \"Auth\": { \"name\": \"A\" } }", "'services' key 'Auth' is not a service key.")]
    [InlineData(", \"services\": { \"auth\": [] }", "'services.auth' must be an object.")]
    [InlineData(", \"services\": { \"auth\": {} }", "'services.auth' needs a name or a description.")]
    [InlineData(", \"services\": { \"auth\": { \"icon\": \"x\" } }", "'services.auth.icon' is not a manifest key.")]
    [InlineData(", \"services\": { \"auth\": { \"name\": \"\" } }", "'services.auth.name' must hold 1–80 characters.")]
    [InlineData(", \"services\": { \"auth\": { \"description\": 3 } }", "'services.auth.description' must be text.")]
    [InlineData(", \"services\": { \"auth\": { \"name\": \"A\" }, \"auth\": { \"name\": \"B\" } }", "'services.auth' is given twice.")]
    [InlineData(", \"services\": { \"auth\": { \"name\": \"A\", \"name\": \"B\" } }", "'services.auth.name' is given twice.")]
    public void Parse_ShouldIgnoreTheFileWithAReason_WhenAServiceIsWrong(string fields, string reason)
    {
        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest(fields)));

        result.Manifest.Should().BeNull();
        result.Problem.Should().Be(reason);
    }

    [Fact]
    public void Parse_ShouldIgnoreTheFile_WhenItListsMoreThanThirtyTwoServices()
    {
        var services = string.Join(", ", Enumerable.Range(0, 33).Select(index => $"\"s{index}\": {{ \"name\": \"S\" }}"));

        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest($", \"services\": {{ {services} }}")));

        result.Problem.Should().Be("'services' lists more than 32 services.");
    }

    [Fact]
    public void Parse_ShouldIgnoreTheFile_WhenItIsLargerThanTheLimit()
    {
        var padding = new string(' ', ProductManifestParser.MaxBytes);

        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Minimal + padding));

        result.Problem.Should().Be("The file is larger than 16 KiB.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"manifest\": 1,")]
    [InlineData("{ \"manifest\": 1, } ")]
    [InlineData("{ /* c */ \"manifest\": 1 }")]
    public void Parse_ShouldIgnoreTheFileWithoutQuotingIt_WhenItIsNotValidJson(string text)
    {
        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(text));

        result.Manifest.Should().BeNull();
        result.Problem.Should().StartWith("The file is not valid JSON");
        result.Problem.Should().NotContain("not json");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"Pilot\"")]
    [InlineData("null")]
    public void Parse_ShouldIgnoreTheFile_WhenTheRootIsNotAnObject(string text)
    {
        ProductManifestParser.Parse(Encoding.UTF8.GetBytes(text)).Problem.Should().Be("The file must hold one JSON object.");
    }

    [Fact]
    public void Parse_ShouldIgnoreTheFile_WhenItIsNotUtf8()
    {
        byte[] bytes = [.. "{ \"manifest\": 1, \"name\": \""u8, 0xFF, 0xFE, .. "\", \"description\": \"x\" }"u8];

        ProductManifestParser.Parse(bytes).Manifest.Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldShortenAndCleanAnUnknownKey_WhenAReasonNamesIt()
    {
        var key = "x\\u0007" + new string('k', 80);

        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest($", \"{key}\": 1")));

        result.Problem.Should().Be("'x?" + new string('k', 38) + "…' is not a manifest key.");
    }

    [Fact]
    public void Parse_ShouldIgnoreTheFile_WhenItNestsTooDeep()
    {
        var nested = string.Concat(Enumerable.Repeat("[", 20)) + string.Concat(Enumerable.Repeat("]", 20));

        var result = ProductManifestParser.Parse(Encoding.UTF8.GetBytes(Manifest($", \"services\": {nested}")));

        result.Problem.Should().StartWith("The file is not valid JSON");
    }
}
