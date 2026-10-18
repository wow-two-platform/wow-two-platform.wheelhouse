using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Products;

/// <summary>Reads and validates a product manifest — the wow-two product manifest convention,
/// <c>conventions/design/identity/product-manifest.md</c>, and its schema <c>product.schema.json</c>.</summary>
/// <remarks>
/// The file lives at <see cref="Path"/> in the product's repository. It holds the identity text the console shows: name,
/// description, homepage and each service's name and description. A file that breaks the schema is ignored whole with
/// one display-safe reason; the reason never quotes the file beyond a key name, which is shortened and stripped of
/// control characters. The rules here and in the schema must stay the same.
/// </remarks>
public static partial class ProductManifestParser
{
    /// <summary>The manifest's path, relative to the repository root.</summary>
    public const string Path = "product/product.json";

    /// <summary>The largest manifest Wheelhouse reads, in bytes.</summary>
    public const int MaxBytes = 16 * 1024;

    /// <summary>The manifest format this parser reads.</summary>
    public const int Format = 1;

    private const int MaxNameLength = 80;
    private const int MaxDescriptionLength = 200;
    private const int MaxHomepageLength = 300;
    private const int MaxServices = 32;
    private const int MaxShownKeyLength = 40;
    private static readonly string[] ServiceKeys = ["name", "description"];

    /// <summary>Validates a manifest file's bytes.</summary>
    /// <param name="content">The file as read from the repository.</param>
    /// <returns>The manifest it holds, or the reason it is ignored.</returns>
    public static ProductManifestParse Parse(ReadOnlyMemory<byte> content)
    {
        if (content.Length > MaxBytes)
            return ProductManifestParse.Invalid("The file is larger than 16 KiB.");
        if (content.Span.StartsWith(Encoding.UTF8.Preamble))
            content = content[Encoding.UTF8.Preamble.Length..];

        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 8 });
            return Read(document.RootElement);
        }
        catch (JsonException exception)
        {
            var line = exception.LineNumber is { } number ? $" (line {number + 1})" : "";
            return ProductManifestParse.Invalid($"The file is not valid JSON{line}.");
        }
        catch (InvalidOperationException)
        {
            return ProductManifestParse.Invalid("The file is not valid JSON text.");
        }
    }

    private static ProductManifestParse Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return ProductManifestParse.Invalid("The file must hold one JSON object.");

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("$schema" or "manifest" or "name" or "description" or "homepage" or "services"))
                return ProductManifestParse.Invalid($"'{Shown(property.Name)}' is not a manifest key.");
            if (!fields.TryAdd(property.Name, property.Value))
                return ProductManifestParse.Invalid($"'{property.Name}' is given twice.");
        }

        if (fields.TryGetValue("$schema", out var schema) && schema.ValueKind != JsonValueKind.String)
            return ProductManifestParse.Invalid("'$schema' must be text.");
        if (!fields.TryGetValue("manifest", out var format))
            return ProductManifestParse.Invalid("'manifest' is required.");
        if (format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var version) || version != Format)
            return ProductManifestParse.Invalid($"'manifest' must be {Format}.");

        if (!fields.TryGetValue("name", out var nameValue))
            return ProductManifestParse.Invalid("'name' is required.");
        if (Text(nameValue, "name", MaxNameLength, out var name) is { } nameProblem)
            return ProductManifestParse.Invalid(nameProblem);
        if (!fields.TryGetValue("description", out var descriptionValue))
            return ProductManifestParse.Invalid("'description' is required.");
        if (Text(descriptionValue, "description", MaxDescriptionLength, out var description) is { } descriptionProblem)
            return ProductManifestParse.Invalid(descriptionProblem);

        string? homepage = null;
        if (fields.TryGetValue("homepage", out var homepageValue))
        {
            if (Homepage(homepageValue, out homepage) is { } homepageProblem)
                return ProductManifestParse.Invalid(homepageProblem);
        }

        IReadOnlyList<ProductServiceIdentityValueObject> services = [];
        if (fields.TryGetValue("services", out var servicesValue))
        {
            if (Services(servicesValue, out services) is { } servicesProblem)
                return ProductManifestParse.Invalid(servicesProblem);
        }

        return ProductManifestParse.Valid(new ProductManifest(name!, description!, homepage, services));
    }

    /// <summary>Checks one text field; returns the problem, or <see langword="null"/> and the text.</summary>
    private static string? Text(JsonElement value, string label, int maxLength, out string? text)
    {
        text = null;
        if (value.ValueKind != JsonValueKind.String)
            return $"'{label}' must be text.";

        var candidate = value.GetString()!;
        var length = candidate.EnumerateRunes().Count();
        if (length < 1 || length > maxLength)
            return $"'{label}' must hold 1–{maxLength} characters.";
        if (!OneLine().IsMatch(candidate))
            return $"'{label}' must be one line without leading or trailing space.";

        text = candidate;
        return null;
    }

    private static string? Homepage(JsonElement value, out string? homepage)
    {
        homepage = null;
        if (value.ValueKind != JsonValueKind.String)
            return "'homepage' must be text.";

        var candidate = value.GetString()!;
        if (candidate.EnumerateRunes().Count() > MaxHomepageLength || !Address().IsMatch(candidate))
            return "'homepage' must be an http or https address without credentials, at most 300 characters.";

        homepage = candidate;
        return null;
    }

    private static string? Services(JsonElement value, out IReadOnlyList<ProductServiceIdentityValueObject> services)
    {
        services = [];
        if (value.ValueKind != JsonValueKind.Object)
            return "'services' must be an object.";

        var found = new SortedDictionary<string, ProductServiceIdentityValueObject>(StringComparer.Ordinal);
        foreach (var service in value.EnumerateObject())
        {
            if (!ServiceKey().IsMatch(service.Name))
                return $"'services' key '{Shown(service.Name)}' is not a service key.";
            if (found.ContainsKey(service.Name))
                return $"'services.{service.Name}' is given twice.";
            if (found.Count >= MaxServices)
                return $"'services' lists more than {MaxServices} services.";
            if (Service(service.Name, service.Value, out var identity) is { } problem)
                return problem;
            found.Add(service.Name, identity!);
        }

        services = [.. found.Values];
        return null;
    }

    private static string? Service(string key, JsonElement value, out ProductServiceIdentityValueObject? identity)
    {
        identity = null;
        var label = $"services.{key}";
        if (value.ValueKind != JsonValueKind.Object)
            return $"'{label}' must be an object.";

        string? name = null;
        string? description = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!ServiceKeys.Contains(property.Name, StringComparer.Ordinal))
                return $"'{label}.{Shown(property.Name)}' is not a manifest key.";
            if (!seen.Add(property.Name))
                return $"'{label}.{property.Name}' is given twice.";

            var problem = property.Name == "name"
                ? Text(property.Value, $"{label}.name", MaxNameLength, out name)
                : Text(property.Value, $"{label}.description", MaxDescriptionLength, out description);
            if (problem is not null)
                return problem;
        }

        if (name is null && description is null)
            return $"'{label}' needs a name or a description.";

        identity = new ProductServiceIdentityValueObject { Service = key, Name = name, Description = description };
        return null;
    }

    /// <summary>Shortens a key from the file and replaces control characters, so a reason can name it safely.</summary>
    private static string Shown(string key)
    {
        var shown = new StringBuilder();
        foreach (var rune in key.EnumerateRunes())
        {
            if (shown.Length >= MaxShownKeyLength)
                return shown.Append('…').ToString();
            shown.Append(Rune.IsControl(rune) ? '?' : rune.ToString());
        }

        return shown.ToString();
    }

    // The patterns of `product.schema.json`; `\z` stands for its `$`, since .NET's `$` also matches before a final newline.
    [GeneratedRegex(@"^\S(?:[^\u0000-\u001f\u007f]*\S)?\z")]
    private static partial Regex OneLine();

    [GeneratedRegex(@"^https?://[^\s@/?#]+(?:[/?#]\S*)?\z")]
    private static partial Regex Address();

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,47}\z")]
    private static partial Regex ServiceKey();
}
