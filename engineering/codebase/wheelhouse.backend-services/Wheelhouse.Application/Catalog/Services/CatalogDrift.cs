using System.Text.RegularExpressions;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Products;
using Wheelhouse.Domain.Catalog.Enums;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Catalog.Services;

/// <summary>Says where an existing product differs from its catalog entry in what the catalog owns: its workspace, what
/// deploys it, and, for an entry Wheelhouse deploys, its repositories and a release source for each. An import brings
/// each back in step; the product's name, description, environments, settings and any release source it already has
/// stay the owner's.</summary>
public static class CatalogDrift
{
    /// <summary>Holds the change of workspace.</summary>
    public const string Workspace = "workspace";

    /// <summary>Holds the change of what deploys the product.</summary>
    public const string Deploy = "deploy";

    /// <summary>Holds the change of the product's repositories.</summary>
    public const string Repositories = "repositories";

    /// <summary>Holds a release source derived for a repository that has none.</summary>
    public const string Release = "release";

    /// <summary>The deploy mode a product takes from its catalog entry's <c>deploy</c>.</summary>
    /// <param name="deploy">The entry's <c>deploy</c>.</param>
    /// <returns>The product's deploy mode.</returns>
    public static ProductDeployMode ModeOf(CatalogDeploy deploy) => deploy switch
    {
        CatalogDeploy.Ci => ProductDeployMode.Ci,
        CatalogDeploy.None => ProductDeployMode.None,
        _ => ProductDeployMode.Wheelhouse,
    };

    /// <summary>Whether the entry's repositories are ones a product takes: Wheelhouse deploys it and each is an
    /// <c>owner/name</c> the inventory accepts.</summary>
    /// <param name="entry">The catalog entry.</param>
    /// <returns><c>true</c> when an import may give the product these repositories and their release sources.</returns>
    public static bool OwnsRepositories(CatalogEntryModel entry) =>
        entry.Deploy == CatalogDeploy.Wheelhouse && entry.Repositories.Count > 0
        && entry.Repositories.All(repository => Regex.IsMatch(repository, InventoryPatternConstants.Repository));

    /// <summary>Whether the entry lists other repositories than the product has, or the same ones in another order.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="entry">Its catalog entry.</param>
    /// <returns><c>true</c> when an import gives the product the entry's repositories.</returns>
    public static bool RepositoriesDiffer(ProductEntity product, CatalogEntryModel entry) =>
        OwnsRepositories(entry)
        && !ProductRepositories.Of(product).Select(item => item.Repository).SequenceEqual(entry.Repositories, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether Wheelhouse deploys the entry and one of the product's repositories has no release source, as a
    /// product the catalog once said CI deploys has none.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="entry">Its catalog entry.</param>
    /// <returns><c>true</c> when an import derives the missing release sources.</returns>
    public static bool LacksRelease(ProductEntity product, CatalogEntryModel entry) =>
        OwnsRepositories(entry) && ProductRepositories.Of(product).Any(item => item.Release is null);

    /// <summary>Lists what an import would change of an existing product, in a fixed order.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="workspace">The slug of the product's workspace, or <c>null</c> when it cannot be named.</param>
    /// <param name="entry">Its catalog entry.</param>
    /// <returns>Each of <see cref="Workspace"/>, <see cref="Deploy"/>, <see cref="Repositories"/> and
    /// <see cref="Release"/> that differs; empty when the product is in step.</returns>
    public static IReadOnlyList<string> Of(ProductEntity product, string? workspace, CatalogEntryModel entry)
    {
        var changes = new List<string>();
        if (!string.Equals(workspace, entry.Workspace, StringComparison.Ordinal))
            changes.Add(Workspace);
        if (product.Deploy != ModeOf(entry.Deploy))
            changes.Add(Deploy);
        if (RepositoriesDiffer(product, entry))
            changes.Add(Repositories);
        else if (LacksRelease(product, entry))
            changes.Add(Release);
        return changes;
    }
}
