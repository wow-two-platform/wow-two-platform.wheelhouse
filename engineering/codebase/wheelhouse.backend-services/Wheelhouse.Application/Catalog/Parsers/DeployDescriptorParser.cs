using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Inventory.Constants;
using YamlDotNet.RepresentationModel;

namespace Wheelhouse.Application.Catalog.Parsers;

/// <summary>Parses the parts of a product's <c>deploy.yml</c> Wheelhouse reads: <c>product</c>, each service's key,
/// optional <c>image</c> and <c>needs</c>, the sites the product publishes with their exposure — descriptor 2's
/// top-level <c>sites</c>, or descriptor 1's per-service sites, where a site any service makes private is private — and
/// the optional top-level <c>extensions</c>, the untrusted PostgreSQL extensions its databases take. The
/// shared publish workflow validates the rest; this reader ignores it. A refusal names the line and the field.</summary>
public static class DeployDescriptorParser
{
    private const string Exposures = "^(public|private)$";

    /// <summary>A PostgreSQL extension's name as <c>CREATE EXTENSION</c> takes it quoted, such as <c>uuid-ossp</c>.</summary>
    private const string ExtensionName = @"^[a-z][a-z0-9_-]{0,62}\z";

    /// <summary>Parses a deployment descriptor.</summary>
    /// <param name="content">The file's bytes, UTF-8.</param>
    /// <returns>The product, its services and its sites, or the first problem.</returns>
    public static YamlParse<DeployDescriptorModel> Parse(byte[] content)
    {
        const string file = "deploy.yml";
        try
        {
            var root = YamlReader.Mapping(YamlReader.Load(content, file), "the descriptor");
            var product = YamlReader.Matching(
                YamlReader.Required(root, "product", "product"), "product", InventoryPatternConstants.Slug, "a lowercase slug");
            var services = YamlReader.Mapping(YamlReader.Required(root, "services", "services"), "services");
            if (services.Children.Count == 0)
                throw YamlReader.Refuse(services, "services", "must name at least one service");
            var second = YamlReader.Get(root, "descriptor") is YamlScalarNode { Value: "2" };

            var found = new List<DeployDescriptorService>();
            // Descriptor 1: each service's own sites, by service; a site is private when any service makes it so.
            var perService = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var (key, value) in services.Children)
            {
                var name = YamlReader.Matching(key, "services", InventoryPatternConstants.Slug, "keyed by lowercase slugs");
                var field = "services." + name;
                var service = value as YamlMappingNode;
                var image = service is not null && YamlReader.Get(service, "image") is { } imageNode
                    ? YamlReader.Text(imageNode, field + ".image")
                    : null;
                found.Add(new DeployDescriptorService(name, image, Needs(service, field)));
                if (!second && service is not null && YamlReader.Get(service, "sites") is { } sitesNode)
                {
                    var sites = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (siteKey, siteValue) in YamlReader.Mapping(sitesNode, field + ".sites").Children)
                    {
                        var site = YamlReader.Matching(siteKey, field + ".sites", InventoryPatternConstants.Slug, "keyed by lowercase slugs");
                        sites[site] = Exposure(Site(siteValue, field + ".sites." + site), field + ".sites." + site);
                    }
                    perService[name] = sites;
                }
            }

            return YamlParse<DeployDescriptorModel>.Ok(
                new DeployDescriptorModel(product, found, second ? Roles(root) : Merged(perService))
                {
                    Extensions = Extensions(root),
                });
        }
        catch (YamlRefusal refusal)
        {
            return YamlParse<DeployDescriptorModel>.Fail(refusal.Message);
        }
    }

    /// <summary>Descriptor 2's sites, in file order; the front door is the one marked primary, else the first.</summary>
    private static List<DeployDescriptorSite> Roles(YamlMappingNode root)
    {
        if (YamlReader.Get(root, "sites") is not { } node)
            return [];
        var sites = new List<DeployDescriptorSite>();
        foreach (var (key, value) in YamlReader.Mapping(node, "sites").Children)
        {
            var name = YamlReader.Matching(key, "sites", InventoryPatternConstants.Slug, "keyed by lowercase slugs");
            var role = Site(value, "sites." + name);
            var primary = role is not null && YamlReader.Get(role, "primary") is { } flag
                ? YamlReader.Matching(flag, "sites." + name + ".primary", "^(true|false)$", "true or false") == "true"
                : false;
            sites.Add(new DeployDescriptorSite(name, Exposure(role, "sites." + name), primary));
        }

        return sites.Count > 0 && !sites.Any(site => site.Primary) ? [sites[0] with { Primary = true }, .. sites[1..]] : sites;
    }

    /// <summary>Descriptor 1's sites by name: private when any service makes it private. The front door is the first
    /// site of the first service by name, as the runner picks it for a release that marks none.</summary>
    private static List<DeployDescriptorSite> Merged(SortedDictionary<string, SortedDictionary<string, string>> perService)
    {
        var primary = perService.Values.SelectMany(sites => sites.Keys).FirstOrDefault();
        return
        [
            .. perService.Values.SelectMany(sites => sites)
                .GroupBy(site => site.Key, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new DeployDescriptorSite(
                    group.Key, group.Any(site => site.Value == "private") ? "private" : "public", group.Key == primary)),
        ];
    }

    /// <summary>A service's platform needs, such as <c>postgres</c>; none when it declares none.</summary>
    private static List<string> Needs(YamlMappingNode? service, string field)
    {
        if (service is null || YamlReader.Get(service, "needs") is not { } node)
            return [];
        return
        [
            .. YamlReader.Sequence(node, field + ".needs").Children
                .Select(need => YamlReader.Matching(need, field + ".needs", InventoryPatternConstants.Slug, "lowercase words"))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>The untrusted PostgreSQL extensions the top-level <c>extensions</c> declares, once each, in file order;
    /// none when it declares none.</summary>
    private static List<string> Extensions(YamlMappingNode root)
    {
        if (YamlReader.Get(root, "extensions") is not { } node)
            return [];
        return
        [
            .. YamlReader.Sequence(node, "extensions").Children
                .Select(extension => YamlReader.Matching(extension, "extensions", ExtensionName, "lowercase extension names"))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>A site's declaration: a mapping, or nothing for <c>app:</c> and <c>app: {}</c>.</summary>
    private static YamlMappingNode? Site(YamlNode value, string field) => value switch
    {
        YamlMappingNode mapping => mapping,
        YamlScalarNode { Value: null or "" or "~" or "null" } => null,
        _ => throw YamlReader.Refuse(value, field, "must be a mapping"),
    };

    /// <summary>A site's exposure; <c>public</c> unless it says <c>private</c>.</summary>
    private static string Exposure(YamlMappingNode? site, string field) =>
        site is not null && YamlReader.Get(site, "exposure") is { } node
            ? YamlReader.Matching(node, field + ".exposure", Exposures, "public or private")
            : "public";
}
