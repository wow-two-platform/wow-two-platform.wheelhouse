using System.Text;
using AwesomeAssertions;
using Wheelhouse.Application.Deployments.FilesMigrations.Commands;
using Wheelhouse.Application.Deployments.FilesMigrations.Services;
using Wheelhouse.Application.Deployments.FilesMigrations.Validators;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>Unit tests for the files migration manifest: its validation per kind, the wave and id picking, and the
/// request's URL and step rules.</summary>
public sealed class FilesMigrationManifestTests
{
    private const string Volume = "  - id: {0}\n    {1}kind: volume-folder\n    volume: v\n    path: p\n    reference: r\n";

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Parse_ShouldReadEveryKind_WithKeyAsStringOrList()
    {
        var parsed = FilesMigrationManifestParser.Parse(Bytes("""
            tenant: ocharo
            sources:
              - { id: a, wave: 2, kind: volume-folder, volume: v, path: p, reference: r }
              - id: b
                kind: postgres-column
                database: d
                table: t
                key: [id, rev]
                content: c
                reference: r
                name: file_name
                type: content_type
              - id: c
                kind: sqlite-column
                volume: v
                database: x.db
                table: t
                key: id
                content: c
                reference: r
            """));

        parsed.Problem.Should().BeNull();
        var manifest = parsed.Value!;
        manifest.Tenant.Should().Be("ocharo");
        manifest.Sources.Select(source => (source.Id, source.Wave, source.Kind)).Should()
            .Equal(("a", 2, "volume-folder"), ("b", null, "postgres-column"), ("c", null, "sqlite-column"));
        manifest.Sources[1].Fields["key"].Should().BeEquivalentTo(new List<string> { "id", "rev" });
        manifest.Sources[2].Fields["key"].Should().Be("id");
        manifest.Sources[1].Fields.Keys.Should().Contain(["name", "type"]);
        manifest.Sources[0].Fields["wave"].Should().Be(2);
    }

    [Theory]
    [InlineData("sources: []\n", "tenant")]
    [InlineData("tenant: ocharo\n", "sources")]
    [InlineData("tenant: Not A Name\nsources: []\n", "tenant")]
    [InlineData("tenant: t\nsources:\n  - id: a\n    kind: volume-folder\n    volume: v\n    path: p\n", "reference")]
    [InlineData("tenant: t\nsources:\n  - id: a\n    kind: postgres-column\n    database: d\n    table: t\n    content: c\n    reference: r\n", "key")]
    [InlineData("tenant: t\nsources:\n  - id: a\n    kind: sqlite-column\n    database: d\n    table: t\n    key: k\n    content: c\n    reference: r\n", "volume")]
    [InlineData("tenant: t\nsources:\n  - id: a\n    kind: ftp\n", "kind")]
    [InlineData("tenant: t\nsources:\n  - id: a\n    wave: 0\n    kind: volume-folder\n    volume: v\n    path: p\n    reference: r\n", "wave")]
    [InlineData("tenant: t\nsources:\n  - id: a\n    wave: one\n    kind: volume-folder\n    volume: v\n    path: p\n    reference: r\n", "wave")]
    [InlineData("tenant: t\nsources:\n  - id: Bad Id\n    kind: volume-folder\n    volume: v\n    path: p\n    reference: r\n", "id")]
    [InlineData("tenant: t\nsources:\n  - id: a\n    kind: postgres-column\n    database: d\n    table: t\n    key: []\n    content: c\n    reference: r\n", "key")]
    public void Parse_ShouldRefuseAnInvalidManifest_NamingTheField(string yaml, string field)
    {
        var parsed = FilesMigrationManifestParser.Parse(Bytes(yaml));

        parsed.Value.Should().BeNull();
        parsed.Problem.Should().Contain(field);
    }

    [Fact]
    public void Parse_ShouldRefuseARepeatedId() =>
        FilesMigrationManifestParser.Parse(Bytes("tenant: t\nsources:\n" + string.Format(Volume, "a", "") + string.Format(Volume, "a", "")))
            .Problem.Should().Contain("repeats an id");

    [Fact]
    public void Pick_ShouldOrderByWaveThenManifestOrder_AndNarrowByIds()
    {
        var parsed = FilesMigrationManifestParser.Parse(Bytes("tenant: t\nsources:\n"
            + string.Format(Volume, "late", "wave: 2\n    ") + string.Format(Volume, "none", "")
            + string.Format(Volume, "first", "wave: 1\n    ") + string.Format(Volume, "second", "wave: 1\n    ")));
        var sources = parsed.Value!.Sources;

        FilesMigrationManifestParser.Pick(sources, null, null).Select(source => source.Id).Should().Equal("first", "second", "late", "none");
        FilesMigrationManifestParser.Pick(sources, 1, null).Select(source => source.Id).Should().Equal("first", "second");
        FilesMigrationManifestParser.Pick(sources, null, ["none", "late"]).Select(source => source.Id).Should().Equal("late", "none");
        FilesMigrationManifestParser.Pick(sources, 2, ["first"]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://files.example/", true)]
    [InlineData("https://files.example:8443/api/", true)]
    [InlineData("https://files.example", false)]
    [InlineData("http://files.example/", false)]
    [InlineData("https://u:p@files.example/", false)]
    [InlineData("https://files.example/?x=1/", false)]
    [InlineData("files.example/", false)]
    public void Url_ShouldBeHttpsAndEndWithASlash(string url, bool valid) =>
        FilesMigrationCommandValidator.IsFilesUrl(url).Should().Be(valid);

    [Fact]
    public void Validator_ShouldTakeOnlyKnownStepsAndWaves()
    {
        var validator = new FilesMigrationCommandValidator();
        FilesMigrationCommand Command(string step, int? wave = null, string? import = null) =>
            new("foreverpin-dev", step, wave, null, "files-dev", "https://files.example/", import, "me");

        validator.Validate(Command("plan")).IsValid.Should().BeTrue();
        validator.Validate(Command("push", 1)).IsValid.Should().BeTrue();
        validator.Validate(Command("verify", null, "imp-1")).IsValid.Should().BeTrue();
        validator.Validate(Command("verify")).IsValid.Should().BeFalse("a verify names its import");
        validator.Validate(Command("delete")).IsValid.Should().BeFalse();
        validator.Validate(Command("plan", 0)).IsValid.Should().BeFalse();
        validator.Validate(Command("push", null, "a b")).IsValid.Should().BeFalse();
    }
}
