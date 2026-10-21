using System.Text.RegularExpressions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Products;

/// <summary>The rules of a product built from several repositories (decision 70), as the runner's <c>catalog.py</c> and
/// <c>release_sets.py</c> apply them: its repositories in order, primary first; each repository's short name; and that no
/// two repositories declare one service. Sites follow the same rule, checked where the descriptors are read.</summary>
public static partial class ProductRepositories
{
    /// <summary>Lists every repository of a product with its release source, primary first.</summary>
    /// <param name="product">The stored product.</param>
    /// <returns>Each repository and its release source, or <c>null</c> where it has none.</returns>
    public static IReadOnlyList<(string Repository, ProductReleaseValueObject? Release)> Of(ProductEntity product) =>
        Of(product.Repository, product.Release, product.Components);

    /// <summary>Lists a primary repository and further repositories as one list, primary first.</summary>
    /// <param name="repository">The primary repository.</param>
    /// <param name="release">The primary repository's release source.</param>
    /// <param name="components">The further repositories.</param>
    /// <returns>Each repository and its release source.</returns>
    public static IReadOnlyList<(string Repository, ProductReleaseValueObject? Release)> Of(
        string repository, ProductReleaseValueObject? release, IEnumerable<ProductComponentValueObject>? components) =>
        [(repository, release), .. (components ?? []).Select(component => (component.Repository, component.Release))];

    /// <summary>Names a repository within its product: its name without its owner's or the product's prefix
    /// (<c>ocharo-hq/ocharo-studio</c> → <c>studio</c>), lowercase, in slug characters — the name a release set's
    /// label and scoped sites carry.</summary>
    /// <param name="product">The product's slug.</param>
    /// <param name="repository">The repository, as <c>owner/name</c>.</param>
    /// <returns>The short name.</returns>
    public static string ShortName(string product, string repository)
    {
        var slash = repository.LastIndexOf('/');
        var owner = slash < 0 ? "" : repository[..slash].ToLowerInvariant();
        var name = repository[(slash + 1)..].ToLowerInvariant();
        foreach (var prefix in new[] { owner, product })
        {
            foreach (var separator in new[] { '-', '.', '_' })
            {
                if (prefix.Length > 0 && name.StartsWith(prefix + separator, StringComparison.Ordinal) && name.Length > prefix.Length + 1)
                {
                    name = name[(prefix.Length + 1)..];
                    break;
                }
            }
        }

        name = NotSlug().Replace(name, "-").Trim('-');
        if (name.Length == 0)
            return "repository";
        return char.IsAsciiLetter(name[0]) ? name : "r" + name;
    }

    /// <summary>Says which service two repositories of a product both declare, or <c>null</c> when none does.</summary>
    /// <param name="repositories">Each repository and its release source.</param>
    /// <returns>A display-safe refusal naming the service and both repositories, or <c>null</c>.</returns>
    public static string? ServiceClash(IEnumerable<(string Repository, ProductReleaseValueObject? Release)> repositories)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (repository, release) in repositories)
        {
            foreach (var image in release?.Images ?? [])
            {
                if (owners.TryGetValue(image.Service, out var owner))
                    return $"Service '{image.Service}' is in both {owner} and {repository}; a service name belongs to one repository of the product.";
                owners[image.Service] = repository;
            }
        }

        return null;
    }

    /// <summary>Says why a product's repositories cannot stand together, or <c>null</c> when they can: each repository
    /// once, distinct short names, and no service in two of them.</summary>
    /// <param name="product">The product's slug.</param>
    /// <param name="repositories">Each repository and its release source, primary first.</param>
    /// <returns>A display-safe refusal, or <c>null</c>.</returns>
    public static string? Problem(string product, IReadOnlyList<(string Repository, ProductReleaseValueObject? Release)> repositories)
    {
        if (repositories.Select(item => item.Repository.ToLowerInvariant()).Distinct().Count() != repositories.Count)
            return "A product names each repository once.";
        if (repositories.Select(item => ShortName(product, item.Repository)).Distinct().Count() != repositories.Count)
            return "Two repositories of the product share a short name.";
        return ServiceClash(repositories);
    }

    [GeneratedRegex("[^a-z0-9-]+")]
    private static partial Regex NotSlug();
}
