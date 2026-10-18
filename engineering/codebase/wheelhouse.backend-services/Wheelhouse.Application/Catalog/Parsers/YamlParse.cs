namespace Wheelhouse.Application.Catalog.Parsers;

/// <summary>Represents the outcome of parsing a YAML file: the value, or one display-safe problem that names the line
/// and the field.</summary>
/// <typeparam name="T">The parsed shape.</typeparam>
/// <param name="Value">The parsed value, when the file holds one.</param>
/// <param name="Problem">Why the file was refused, such as <c>line 12: entries[3].deploy must be …</c>.</param>
public sealed record YamlParse<T>(T? Value, string? Problem) where T : class
{
    /// <summary>Wraps a parsed value.</summary>
    public static YamlParse<T> Ok(T value) => new(value, null);

    /// <summary>Wraps a refusal.</summary>
    public static YamlParse<T> Fail(string problem) => new(null, problem);
}
