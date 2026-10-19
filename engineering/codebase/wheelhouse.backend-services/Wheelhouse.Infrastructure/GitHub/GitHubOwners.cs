using System.Text.RegularExpressions;

namespace Wheelhouse.Infrastructure.GitHub;

/// <summary>Reads the GitHub account a repository reference or an API address names, lowercased, since GitHub treats
/// account names without case.</summary>
public static partial class GitHubOwners
{
    /// <summary>Gets the owner of an <c>{owner}/{repo}</c> reference.</summary>
    /// <param name="repository">The repository reference.</param>
    /// <returns>The owner, or <c>null</c> when the reference is not one.</returns>
    public static string? FromRepository(string? repository) =>
        repository is not null && RepositoryPattern().Match(repository) is { Success: true } match
            ? match.Groups[1].Value.ToLowerInvariant()
            : null;

    /// <summary>Gets the owner an <c>https://api.github.com</c> address names in <c>/repos/</c>, <c>/users/</c> or
    /// <c>/orgs/</c>.</summary>
    /// <param name="address">The request address.</param>
    /// <returns>The owner, or <c>null</c> for any other address.</returns>
    public static string? FromApiAddress(Uri? address) =>
        address is { IsAbsoluteUri: true, Scheme: "https", Host: "api.github.com" }
        && ApiPathPattern().Match(address.AbsolutePath) is { Success: true } match
            ? match.Groups[1].Value.ToLowerInvariant()
            : null;

    /// <summary>Gets the repository a read finds as a whole — its branch heads, commits, trees, releases or branches,
    /// rather than one file or run in it — when the address is one; a 404 there means the repository cannot be seen.</summary>
    /// <param name="address">The request address.</param>
    /// <returns>The lowercase <c>{owner}/{repo}</c>, or <c>null</c>.</returns>
    public static string? RepositoryRead(Uri? address) =>
        FromApiAddress(address) is not null && RepositoryReadPattern().Match(address!.AbsolutePath) is { Success: true } match
            ? match.Groups[1].Value.ToLowerInvariant()
            : null;

    /// <summary>Gets whether a value is a GitHub account name.</summary>
    /// <param name="value">The value.</param>
    public static bool IsOwner(string? value) => value is not null && OwnerPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,38}$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerPattern();

    [GeneratedRegex("^([A-Za-z0-9][A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex("^/repos/([A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9._-]{1,100})(?:/commits(?:/[^/]+)?|/releases|/branches|/git/trees/[^/]+)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryReadPattern();

    [GeneratedRegex("^/(?:repos|users|orgs)/([A-Za-z0-9][A-Za-z0-9-]{0,38})(?:/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ApiPathPattern();
}
