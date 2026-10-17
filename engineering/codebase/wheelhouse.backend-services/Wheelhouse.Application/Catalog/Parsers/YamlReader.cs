using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Wheelhouse.Application.Catalog.Parsers;

/// <summary>Reads YAML through the YamlDotNet representation model, so every refusal can name the line and the field.
/// Unknown keys are left alone. A refusal is a <see cref="YamlRefusal"/> whose message never quotes the file beyond a
/// shortened key.</summary>
internal static class YamlReader
{
    /// <summary>Loads the single document of a YAML file.</summary>
    /// <param name="content">The file's bytes, UTF-8.</param>
    /// <param name="file">The file's name, for messages.</param>
    /// <returns>The document's root node.</returns>
    public static YamlNode Load(byte[] content, string file)
    {
        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(Encoding.UTF8.GetString(content));
            stream.Load(reader);
        }
        catch (YamlException exception)
        {
            throw new YamlRefusal($"line {exception.Start.Line}: {file} is not valid YAML.");
        }

        if (stream.Documents.Count != 1)
            throw new YamlRefusal($"line 1: {file} must hold exactly one YAML document.");
        return stream.Documents[0].RootNode;
    }

    /// <summary>Requires a mapping.</summary>
    public static YamlMappingNode Mapping(YamlNode node, string field) =>
        node as YamlMappingNode ?? throw Refuse(node, field, "must be a mapping");

    /// <summary>Requires a sequence.</summary>
    public static YamlSequenceNode Sequence(YamlNode node, string field) =>
        node as YamlSequenceNode ?? throw Refuse(node, field, "must be a list");

    /// <summary>Gets a key's value, or <c>null</c> when the mapping lacks the key or gives it no value.</summary>
    public static YamlNode? Get(YamlMappingNode mapping, string key)
    {
        foreach (var (name, value) in mapping.Children)
            if (name is YamlScalarNode { Value: { } text } && text == key)
                return IsNull(value) ? null : value;
        return null;
    }

    /// <summary>Requires a key whose value is present.</summary>
    public static YamlNode Required(YamlMappingNode mapping, string key, string field) =>
        Get(mapping, key) ?? throw Refuse(mapping, field, "is required");

    /// <summary>Requires a one-line text value.</summary>
    public static string Text(YamlNode node, string field)
    {
        var text = node is YamlScalarNode { Value: { } value } ? value.Trim() : null;
        if (string.IsNullOrEmpty(text) || text.Contains('\n') || text.Contains('\r'))
            throw Refuse(node, field, "must be one line of text");
        return text;
    }

    /// <summary>Requires a value that matches a pattern.</summary>
    public static string Matching(YamlNode node, string field, string pattern, string shape)
    {
        var text = node is YamlScalarNode { Value: { } value } ? value : null;
        if (text is null || !Regex.IsMatch(text, pattern))
            throw Refuse(node, field, "must be " + shape);
        return text;
    }

    /// <summary>Requires one of a set of lowercase words and returns the matching enum member.</summary>
    public static TEnum Word<TEnum>(YamlNode node, string field) where TEnum : struct, Enum
    {
        var words = Enum.GetNames<TEnum>().Select(name => name.ToLowerInvariant()).ToArray();
        var text = node is YamlScalarNode { Value: { } value } ? value : null;
        if (text is null || !words.Contains(text, StringComparer.Ordinal))
            throw Refuse(node, field, "must be " + string.Join(", ", words[..^1]) + " or " + words[^1]);
        return Enum.Parse<TEnum>(text, ignoreCase: true);
    }

    /// <summary>Builds a refusal at a node.</summary>
    public static YamlRefusal Refuse(YamlNode node, string field, string rule) => new($"line {node.Start.Line}: {field} {rule}.");

    /// <summary>Shortens a key for a message, so a refusal never carries much of the file.</summary>
    public static string Short(string key) => key.Length <= 40 ? key : key[..40] + "…";

    private static bool IsNull(YamlNode node) =>
        node is YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" or "~" or "null" or "Null" or "NULL" };
}

/// <summary>Represents a refused YAML file; its message is operator-safe and names the line and the field.</summary>
/// <param name="message">The refusal.</param>
internal sealed class YamlRefusal(string message) : Exception(message);
