using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Catalog;

/// <summary>Holds one hub Wheelhouse reads the catalog from: a YAML file in a GitHub repository at a ref.</summary>
public sealed record CatalogSourceSettings
{
    /// <summary>Gets or sets the hub repository that holds the catalog, as <c>owner/name</c>.</summary>
    public string Repository { get; set; } = "";

    /// <summary>Gets or sets the catalog's path in the repository.</summary>
    public string Path { get; set; } = "catalog.yml";

    /// <summary>Gets or sets the branch, or other ref, the catalog is read at.</summary>
    public string Ref { get; set; } = "main";

    /// <summary>Gets whether the hub has its shape: a repository, a relative path without <c>..</c> and a branch.</summary>
    public bool IsValid =>
        Regex.IsMatch(Repository ?? "", FormatPatternConstants.GitHubRepository)
        && Regex.IsMatch(Path ?? "", @"^[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$")
        && !Path!.Split('/').Contains("..")
        && Regex.IsMatch(Ref ?? "", FormatPatternConstants.GitBranch);

    /// <summary>Gets the hub as messages name it: <c>owner/name@ref</c>.</summary>
    public string Where => $"{Repository}@{Ref}";
}
