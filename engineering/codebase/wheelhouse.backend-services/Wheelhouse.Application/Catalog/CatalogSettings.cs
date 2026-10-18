using System.Text.RegularExpressions;
using Wheelhouse.Application.Inventory.Constants;

namespace Wheelhouse.Application.Catalog;

/// <summary>Holds where Wheelhouse reads the wow2 catalog from: one YAML file in a GitHub repository, read with the
/// signed-in owner's token. Bound from <c>Catalog</c>.</summary>
public sealed record CatalogSettings
{
    /// <summary>Gets or sets the repository that holds the catalog, as <c>owner/name</c>.</summary>
    public string Repository { get; set; } = "wow-two/wow-two-ws";

    /// <summary>Gets or sets the catalog's path in the repository.</summary>
    public string Path { get; set; } = "catalog.yml";

    /// <summary>Gets or sets the branch, or other ref, the catalog is read at.</summary>
    public string Ref { get; set; } = "main";

    /// <summary>Gets whether every setting has its shape: a repository, a relative path without <c>..</c> and a branch.</summary>
    public bool IsValid =>
        Regex.IsMatch(Repository ?? "", InventoryPatternConstants.Repository)
        && Regex.IsMatch(Path ?? "", @"^[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$")
        && !Path!.Split('/').Contains("..")
        && Regex.IsMatch(Ref ?? "", InventoryPatternConstants.Branch);
}
