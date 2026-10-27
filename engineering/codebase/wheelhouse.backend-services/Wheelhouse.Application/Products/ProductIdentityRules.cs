using System.Text.RegularExpressions;

namespace Wheelhouse.Application.Products;

/// <summary>Holds the rules every identity text follows, wherever a repository declares it: the deploy descriptor's
/// identity keys (<c>conventions/deployment/descriptor/deploy-descriptor.md</c> § Identity) and the retired
/// <c>product/product.json</c>. Each check returns the rule a value breaks, as the end of a sentence, or
/// <see langword="null"/>; the caller names the field.</summary>
public static partial class ProductIdentityRules
{
    /// <summary>The longest name, in characters.</summary>
    public const int MaxNameLength = 80;

    /// <summary>The longest description, in characters.</summary>
    public const int MaxDescriptionLength = 200;

    /// <summary>The longest homepage, in characters.</summary>
    public const int MaxHomepageLength = 300;

    /// <summary>Checks a name or description: one line of 1 to <paramref name="maxLength"/> characters, without
    /// control characters or leading or trailing space.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The longest text allowed.</param>
    /// <returns>The rule the text breaks, or <see langword="null"/>.</returns>
    public static string? Text(string text, int maxLength)
    {
        var length = text.EnumerateRunes().Count();
        if (length < 1 || length > maxLength)
            return $"must hold 1–{maxLength} characters";
        return OneLine().IsMatch(text) ? null : "must be one line without leading or trailing space";
    }

    /// <summary>Checks a homepage: an http or https address without credentials, at most 300 characters.</summary>
    /// <param name="homepage">The address.</param>
    /// <returns>The rule the address breaks, or <see langword="null"/>.</returns>
    public static string? Homepage(string homepage) =>
        homepage.EnumerateRunes().Count() <= MaxHomepageLength && Address().IsMatch(homepage)
            ? null
            : "must be an http or https address without credentials, at most 300 characters";

    /// <summary>Gets whether a key is a service key: a lowercase slug.</summary>
    /// <param name="key">The key.</param>
    public static bool IsServiceKey(string key) => ServiceKey().IsMatch(key);

    // The patterns of the schemas; `\z` stands for their `$`, since .NET's `$` also matches before a final newline.
    [GeneratedRegex(@"^\S(?:[^\u0000-\u001f\u007f]*\S)?\z")]
    private static partial Regex OneLine();

    [GeneratedRegex(@"^https?://[^\s@/?#]+(?:[/?#]\S*)?\z")]
    private static partial Regex Address();

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,47}\z")]
    private static partial Regex ServiceKey();
}
