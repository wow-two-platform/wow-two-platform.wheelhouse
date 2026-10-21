using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Domain.Catalog.Enums;
using YamlDotNet.RepresentationModel;

namespace Wheelhouse.Application.Catalog.Parsers;

/// <summary>Parses a hub's catalog (<c>catalog: 1</c>): its hub, workspaces and entries, each entry's repositories (the
/// primary first; several make one product, decision 70) and the products it <c>replaces</c>. <c>orgs</c>, <c>hubs</c>,
/// an entry's <c>status</c> and <c>local</c>, and any key Wheelhouse does not know are ignored; <c>hub</c> is optional.
/// Every entry's workspace must be declared, and slugs are unique. A refusal names the line and the field.</summary>
public static class CatalogParser
{
    /// <summary>Holds the shape of a repository as the catalog writes it.</summary>
    public const string RepositoryPattern = "^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9._-]+$";

    /// <summary>Parses a catalog file.</summary>
    /// <param name="content">The file's bytes, UTF-8.</param>
    /// <param name="file">The file's name, for messages.</param>
    /// <returns>The workspaces and entries, or the first problem.</returns>
    public static YamlParse<CatalogDocument> Parse(byte[] content, string file = "catalog.yml")
    {
        try
        {
            var root = YamlReader.Mapping(YamlReader.Load(content, file), "the catalog");
            var version = YamlReader.Required(root, "catalog", "catalog");
            if (version is not YamlScalarNode { Value: "1" })
                throw YamlReader.Refuse(version, "catalog", "must be 1");

            var workspaces = new List<CatalogWorkspaceModel>();
            var workspaceNodes = YamlReader.Sequence(YamlReader.Required(root, "workspaces", "workspaces"), "workspaces");
            for (var index = 0; index < workspaceNodes.Children.Count; index++)
            {
                var field = $"workspaces[{index}]";
                var node = YamlReader.Mapping(workspaceNodes.Children[index], field);
                var slugNode = YamlReader.Required(node, "slug", field + ".slug");
                var slug = YamlReader.Matching(slugNode, field + ".slug", InventoryPatternConstants.Slug, "a lowercase slug");
                if (workspaces.Any(workspace => workspace.Slug == slug))
                    throw YamlReader.Refuse(slugNode, field + ".slug", $"repeats the workspace '{slug}'");
                workspaces.Add(new CatalogWorkspaceModel
                {
                    Slug = slug,
                    Name = YamlReader.Text(YamlReader.Required(node, "name", field + ".name"), field + ".name"),
                    Description = YamlReader.Get(node, "description") is { } description
                        ? YamlReader.Text(description, field + ".description")
                        : null,
                });
            }

            var entries = new List<CatalogEntryModel>();
            var entryNodes = YamlReader.Sequence(YamlReader.Required(root, "entries", "entries"), "entries");
            for (var index = 0; index < entryNodes.Children.Count; index++)
                entries.Add(Entry(YamlReader.Mapping(entryNodes.Children[index], $"entries[{index}]"), $"entries[{index}]", workspaces, entries));

            return YamlParse<CatalogDocument>.Ok(new CatalogDocument(workspaces, entries, Hub(root)));
        }
        catch (YamlRefusal refusal)
        {
            return YamlParse<CatalogDocument>.Fail(refusal.Message);
        }
    }

    /// <summary>Reads the optional <c>hub</c>: its slug and name.</summary>
    private static CatalogHubModel? Hub(YamlMappingNode root)
    {
        if (YamlReader.Get(root, "hub") is not { } node)
            return null;

        var hub = YamlReader.Mapping(node, "hub");
        return new CatalogHubModel
        {
            Slug = YamlReader.Matching(YamlReader.Required(hub, "slug", "hub.slug"), "hub.slug", InventoryPatternConstants.Slug, "a lowercase slug"),
            Name = YamlReader.Text(YamlReader.Required(hub, "name", "hub.name"), "hub.name"),
        };
    }

    private static CatalogEntryModel Entry(
        YamlMappingNode node, string field, IReadOnlyList<CatalogWorkspaceModel> workspaces, IReadOnlyList<CatalogEntryModel> earlier)
    {
        var slugNode = YamlReader.Required(node, "slug", field + ".slug");
        var slug = YamlReader.Matching(slugNode, field + ".slug", InventoryPatternConstants.Slug, "a lowercase slug");
        if (earlier.Any(entry => entry.Slug == slug))
            throw YamlReader.Refuse(slugNode, field + ".slug", $"repeats the entry '{slug}'");

        var workspaceNode = YamlReader.Required(node, "workspace", field + ".workspace");
        var workspace = YamlReader.Matching(workspaceNode, field + ".workspace", InventoryPatternConstants.Slug, "a lowercase slug");
        if (workspaces.All(declared => declared.Slug != workspace))
            throw YamlReader.Refuse(workspaceNode, field + ".workspace", $"names '{YamlReader.Short(workspace)}', which workspaces does not declare");

        var repositories = new List<string>();
        if (YamlReader.Get(node, "repositories") is { } repositoryNodes)
        {
            var list = YamlReader.Sequence(repositoryNodes, field + ".repositories");
            for (var index = 0; index < list.Children.Count; index++)
            {
                var repository = YamlReader.Matching(
                    list.Children[index], $"{field}.repositories[{index}]", RepositoryPattern, "a GitHub repository as owner/name");
                if (repositories.Contains(repository, StringComparer.OrdinalIgnoreCase))
                    throw YamlReader.Refuse(list.Children[index], $"{field}.repositories[{index}]", "repeats a repository");
                repositories.Add(repository);
            }
        }

        // Products an entry supersedes, such as the products of the repositories a product built from several merges.
        var replaces = new List<string>();
        if (YamlReader.Get(node, "replaces") is { } replacedNodes)
        {
            var list = YamlReader.Sequence(replacedNodes, field + ".replaces");
            for (var index = 0; index < list.Children.Count; index++)
            {
                var replaced = YamlReader.Matching(
                    list.Children[index], $"{field}.replaces[{index}]", InventoryPatternConstants.Slug, "a lowercase slug");
                if (replaced == slug || replaces.Contains(replaced, StringComparer.Ordinal))
                    throw YamlReader.Refuse(list.Children[index], $"{field}.replaces[{index}]", "names the entry itself or repeats a slug");
                replaces.Add(replaced);
            }
        }

        return new CatalogEntryModel
        {
            Slug = slug,
            Name = YamlReader.Text(YamlReader.Required(node, "name", field + ".name"), field + ".name"),
            Workspace = workspace,
            Kind = YamlReader.Word<CatalogKind>(YamlReader.Required(node, "kind", field + ".kind"), field + ".kind"),
            Deploy = YamlReader.Word<CatalogDeploy>(YamlReader.Required(node, "deploy", field + ".deploy"), field + ".deploy"),
            Purpose = YamlReader.Get(node, "purpose") is { } purpose ? YamlReader.Text(purpose, field + ".purpose") : "",
            Repositories = repositories,
            Replaces = replaces,
        };
    }
}
