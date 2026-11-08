using System.Globalization;
using Wheelhouse.Application.Catalog.Parsers;
using Wheelhouse.Application.Deployments.FilesMigrations.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization.Yaml;
using YamlDotNet.RepresentationModel;

namespace Wheelhouse.Application.Deployments.FilesMigrations.Services;

/// <summary>Parses and validates a repository's <c>engineering/deployment/files-migration.yml</c>: a <c>tenant</c> and
/// <c>sources[]</c> with unique ids, an optional integer <c>wave</c> of 1 or more, and the fields each <c>kind</c> takes.
/// A refusal names the line and the field; no message quotes a value beyond what the YAML reader shows.</summary>
public static class FilesMigrationManifestParser
{
    /// <summary>Holds the manifest's path in a repository.</summary>
    public const string Path = "engineering/deployment/files-migration.yml";

    /// <summary>Holds how large a manifest may be.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>Holds the pattern a tenant and a source id follow.</summary>
    public const string Name = "^[a-z0-9][a-z0-9_-]{0,63}$";

    private const string VolumeFolder = "volume-folder";
    private const string PostgresColumn = "postgres-column";
    private const string SqliteColumn = "sqlite-column";

    private static readonly IReadOnlyDictionary<string, (string[] Required, string[] Optional)> Kinds =
        new Dictionary<string, (string[], string[])>(StringComparer.Ordinal)
        {
            [VolumeFolder] = (["volume", "path", "reference"], []),
            [PostgresColumn] = (["database", "table", "key", "content", "reference"], ["name", "type"]),
            [SqliteColumn] = (["volume", "database", "table", "key", "content", "reference"], ["type"]),
        };

    /// <summary>Parses a manifest.</summary>
    /// <param name="content">The file's bytes, UTF-8.</param>
    /// <returns>The manifest, or the first problem.</returns>
    public static YamlParse<FilesMigrationManifest> Parse(byte[] content)
    {
        const string file = "files-migration.yml";
        try
        {
            var root = YamlReader.Mapping(YamlReader.Load(content, file), "the manifest");
            var tenant = YamlReader.Matching(YamlReader.Required(root, "tenant", "tenant"), "tenant", Name, "a lowercase name");
            var list = YamlReader.Sequence(YamlReader.Required(root, "sources", "sources"), "sources");
            var sources = new List<FilesMigrationSource>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < list.Children.Count; index++)
            {
                var source = Source(list.Children[index], $"sources[{index}]");
                if (!seen.Add(source.Id))
                    throw YamlReader.Refuse(list.Children[index], $"sources[{index}].id", "repeats an id; ids are unique");
                sources.Add(source);
            }

            return YamlParse<FilesMigrationManifest>.Ok(new FilesMigrationManifest(tenant, sources));
        }
        catch (YamlRefusalException refusal)
        {
            return YamlParse<FilesMigrationManifest>.Fail(refusal.Message);
        }
    }

    /// <summary>Picks sources by wave and/or id, in wave then manifest order.</summary>
    /// <param name="sources">Every source of the product, in repository then manifest order.</param>
    /// <param name="wave">The wave to take; null for any.</param>
    /// <param name="ids">The ids to take; null or empty for any.</param>
    /// <returns>The picked sources; empty when none matches.</returns>
    public static IReadOnlyList<FilesMigrationSource> Pick(IReadOnlyList<FilesMigrationSource> sources, int? wave, IReadOnlyCollection<string>? ids) =>
    [
        .. sources
            .Select((source, order) => (source, order))
            .Where(item => (wave is null || item.source.Wave == wave) && (ids is not { Count: > 0 } || ids.Contains(item.source.Id)))
            .OrderBy(item => item.source.Wave ?? int.MaxValue)
            .ThenBy(item => item.order)
            .Select(item => item.source),
    ];

    private static FilesMigrationSource Source(YamlNode node, string field)
    {
        var map = YamlReader.Mapping(node, field);
        var id = YamlReader.Matching(YamlReader.Required(map, "id", field + ".id"), field + ".id", Name, "a lowercase name");
        var kind = YamlReader.Text(YamlReader.Required(map, "kind", field + ".kind"), field + ".kind");
        if (!Kinds.TryGetValue(kind, out var shape))
            throw YamlReader.Refuse(YamlReader.Required(map, "kind", field + ".kind"), field + ".kind",
                "must be volume-folder, postgres-column or sqlite-column");

        var fields = new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = id, ["kind"] = kind };
        int? wave = null;
        if (YamlReader.Get(map, "wave") is { } waveNode)
        {
            if (!int.TryParse(YamlReader.Text(waveNode, field + ".wave"), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
                throw YamlReader.Refuse(waveNode, field + ".wave", "must be an integer of 1 or more");
            wave = number;
            fields["wave"] = number;
        }

        foreach (var name in shape.Required)
            fields[name] = Value(name, YamlReader.Required(map, name, field + "." + name), field + "." + name);
        foreach (var name in shape.Optional)
            if (YamlReader.Get(map, name) is { } optional)
                fields[name] = Value(name, optional, field + "." + name);
        return new FilesMigrationSource(id, wave, kind, fields);
    }

    /// <summary>A field's value: a one-line string, or for <c>key</c> a string or a list of strings.</summary>
    private static object Value(string name, YamlNode node, string field)
    {
        if (name == "key" && node is YamlSequenceNode keys)
        {
            if (keys.Children.Count == 0)
                throw YamlReader.Refuse(node, field, "must name at least one column");
            return keys.Children.Select(child => Line(child, field)).ToList();
        }

        return Line(node, field);
    }

    private static string Line(YamlNode node, string field)
    {
        var text = YamlReader.Text(node, field);
        if (text.Length == 0 || text.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw YamlReader.Refuse(node, field, "must be one non-empty line");
        return text;
    }
}
