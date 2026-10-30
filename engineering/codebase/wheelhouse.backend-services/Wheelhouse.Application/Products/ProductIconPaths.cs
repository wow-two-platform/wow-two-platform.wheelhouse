using System.Text.RegularExpressions;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Products;

/// <summary>Computes where a product's or service's icon lives in its repository and which file to use when a
/// variant is missing — the wow-two product icons convention, <c>conventions/design/identity/product-icons.md</c>.</summary>
/// <remarks>
/// Paths are computed, never discovered: <c>product/brand/icons/{form}-{color}-{theme}.{svg|png}</c> for the product and
/// <c>product/brand/icons/services/{service}/…</c> for a service. A requested variant falls back through the same
/// theme (service folder, then product folder), then the other theme, then the web app's favicon; when nothing
/// resolves the caller shows its monogram.
/// </remarks>
public static partial class ProductIconPaths
{
    /// <summary>The largest icon Wheelhouse reads, in bytes.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>The folder every icon sits under, relative to the repository root.</summary>
    public const string Folder = "product/brand/icons";

    private const string ServicesFolder = Folder + "/services";
    private static readonly string[] Formats = ["svg", "png"];

    /// <summary>Returns every convention path to try for <paramref name="request"/>, best first: the requested theme
    /// (service folder, then product folder) before the other theme, each trying the requested form and color, then
    /// the other color, then — for <see cref="IconForm.Full"/> — the short form; <c>svg</c> before <c>png</c>.</summary>
    /// <param name="request">Which icon is wanted.</param>
    /// <exception cref="ArgumentException">The service is not a service name.</exception>
    public static IReadOnlyList<string> Candidates(ProductIconRequest request)
    {
        if (request.Service is not null && !ServiceName().IsMatch(request.Service))
            throw new ArgumentException("A service name is a lowercase letter then lowercase letters, digits or dashes.", nameof(request));

        var folders = request.Service is null ? [Folder] : new[] { $"{ServicesFolder}/{request.Service}", Folder };
        var themes = new[] { request.Theme, request.Theme == IconTheme.Light ? IconTheme.Dark : IconTheme.Light };
        List<string> paths = [];
        foreach (var theme in themes)
            foreach (var folder in folders)
                foreach (var (form, color) in Variants(request.Form, request.Color))
                    foreach (var format in Formats)
                        paths.Add($"{folder}/{Name(form)}-{Name(color)}-{Name(theme)}.{format}");
        return paths;
    }

    /// <summary>Returns the best file among <paramref name="paths"/> for <paramref name="request"/>, or
    /// <see langword="null"/> when none resolves.</summary>
    /// <param name="paths">The repository's file paths.</param>
    /// <param name="request">Which icon is wanted.</param>
    public static string? Choose(IEnumerable<string> paths, ProductIconRequest request)
    {
        var available = paths as ISet<string> ?? paths.ToHashSet(StringComparer.Ordinal);
        return Candidates(request).FirstOrDefault(available.Contains) ?? FaviconOf(available, request.Theme);
    }

    /// <summary>Tells whether a repository file can ever be chosen — a convention icon or a web app favicon — so a
    /// source keeps only these from a large tree.</summary>
    /// <param name="path">A repository file path.</param>
    public static bool IsCandidate(string path) => IconFile().IsMatch(path) || Favicon().IsMatch(path);

    /// <summary>The media type for an icon path's extension.</summary>
    public static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };

    /// <summary>The variants one folder and theme try, best first: the requested form and color, the other color, then
    /// the short form when the full one was requested.</summary>
    private static IEnumerable<(IconForm Form, IconColor Color)> Variants(IconForm form, IconColor color)
    {
        var other = color == IconColor.Colored ? IconColor.Monochrome : IconColor.Colored;
        yield return (form, color);
        yield return (form, other);
        if (form != IconForm.Full)
            yield break;
        yield return (IconForm.Short, color);
        yield return (IconForm.Short, other);
    }

    /// <summary>The web app's favicon, the last file before the monogram: <c>favicon-dark</c> then <c>favicon</c> on a
    /// dark theme, <c>favicon</c> on a light one; <c>svg</c> before <c>png</c>, then the first path in ordinal order.</summary>
    private static string? FaviconOf(ISet<string> available, IconTheme theme)
    {
        var favicons = available
            .Select(path => (Path: path, Match: Favicon().Match(path)))
            .Where(item => item.Match.Success)
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToList();
        string[] names = theme == IconTheme.Dark ? ["favicon-dark", "favicon"] : ["favicon"];
        foreach (var name in names)
            foreach (var format in Formats)
                foreach (var (path, match) in favicons)
                    if (match.Groups["name"].Value == name && match.Groups["format"].Value == format)
                        return path;
        return null;
    }

    private static string Name(Enum value) => value.ToString().ToLowerInvariant();

    [GeneratedRegex(FormatPatternConstants.NameSlug)]
    private static partial Regex ServiceName();

    [GeneratedRegex(@"^product/brand/icons/(?:services/[a-z][a-z0-9-]{0,47}/)?(?:full|short)-(?:colored|monochrome)-(?:light|dark)\.(?:svg|png)$")]
    private static partial Regex IconFile();

    [GeneratedRegex(@"^engineering/codebase/[^/]+\.frontend-services/apps/web/public/(?<name>favicon-dark|favicon)\.(?<format>svg|png)$")]
    private static partial Regex Favicon();
}
