using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Inventory.Constants;
using YamlDotNet.RepresentationModel;

namespace Wheelhouse.Application.Catalog.Parsers;

/// <summary>Parses the parts of a product's <c>deploy.yml</c> a catalog import needs: <c>product</c> and each service's
/// key and optional <c>image</c>. The shared publish workflow validates the rest; this reader ignores it. A refusal
/// names the line and the field.</summary>
public static class DeployDescriptorParser
{
    /// <summary>Parses a deployment descriptor.</summary>
    /// <param name="content">The file's bytes, UTF-8.</param>
    /// <returns>The product and its services, or the first problem.</returns>
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

            var found = new List<(string, string?)>();
            foreach (var (key, value) in services.Children)
            {
                var name = YamlReader.Matching(key, "services", InventoryPatternConstants.Slug, "keyed by lowercase slugs");
                var field = "services." + name;
                var image = value is YamlMappingNode service && YamlReader.Get(service, "image") is { } imageNode
                    ? YamlReader.Text(imageNode, field + ".image")
                    : null;
                found.Add((name, image));
            }

            return YamlParse<DeployDescriptorModel>.Ok(new DeployDescriptorModel(product, found));
        }
        catch (YamlRefusal refusal)
        {
            return YamlParse<DeployDescriptorModel>.Fail(refusal.Message);
        }
    }
}
