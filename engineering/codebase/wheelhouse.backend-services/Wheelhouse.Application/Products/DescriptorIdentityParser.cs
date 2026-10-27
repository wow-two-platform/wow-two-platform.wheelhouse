using Wheelhouse.Application.Catalog.Parsers;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Models;
using YamlDotNet.RepresentationModel;

namespace Wheelhouse.Application.Products;

/// <summary>Reads the identity a product's <c>engineering/deployment/deploy.yml</c> declares — the deploy descriptor
/// convention's § Identity: <c>name</c>, <c>description</c> and <c>homepage</c> beside <c>product</c>, and each
/// service's own <c>name</c> and <c>description</c>.</summary>
/// <remarks>
/// A descriptor with none of these keys declares no identity, and the caller may fall back to the retired
/// <c>product/product.json</c>. One that declares any must give <c>name</c> and <c>description</c>, and every text
/// follows <see cref="ProductIdentityRules"/>. A refusal names the line and the field, never the value. The rest of the
/// descriptor is the shared publish workflow's to validate; this reader leaves it alone.
/// </remarks>
public static class DescriptorIdentityParser
{
    /// <summary>Reads the identity a descriptor declares.</summary>
    /// <param name="content">The descriptor's bytes, UTF-8.</param>
    /// <returns>The identity, the reason it is ignored, or <see cref="ProductManifestParse.Absent"/>.</returns>
    public static ProductManifestParse Parse(byte[] content)
    {
        try
        {
            var root = YamlReader.Mapping(YamlReader.Load(content, "deploy.yml"), "the descriptor");
            var name = YamlReader.Get(root, "name");
            var description = YamlReader.Get(root, "description");
            var homepage = YamlReader.Get(root, "homepage");
            var services = Services(root);
            if (name is null && description is null && homepage is null && services.Count == 0)
                return ProductManifestParse.Absent;

            var manifest = new ProductManifest(
                Text(name ?? throw YamlReader.Refuse(root, "name", "is required beside description"), "name",
                    ProductIdentityRules.MaxNameLength),
                Text(description ?? throw YamlReader.Refuse(root, "description", "is required beside name"), "description",
                    ProductIdentityRules.MaxDescriptionLength),
                homepage is null ? null : Homepage(homepage),
                services);
            return ProductManifestParse.Valid(manifest);
        }
        catch (YamlRefusal refusal)
        {
            return ProductManifestParse.Invalid(refusal.Message);
        }
    }

    /// <summary>The name and description of each service that declares its own, in key order.</summary>
    private static List<ProductServiceIdentityValueObject> Services(YamlMappingNode root)
    {
        if (YamlReader.Get(root, "services") is not YamlMappingNode services)
            return [];

        var found = new SortedDictionary<string, ProductServiceIdentityValueObject>(StringComparer.Ordinal);
        foreach (var (key, value) in services.Children)
        {
            if (value is not YamlMappingNode service)
                continue;
            var name = YamlReader.Get(service, "name");
            var description = YamlReader.Get(service, "description");
            if (name is null && description is null)
                continue;

            var slug = key is YamlScalarNode { Value: { } text } && ProductIdentityRules.IsServiceKey(text)
                ? text
                : throw YamlReader.Refuse(key, "services", "must be keyed by lowercase slugs");
            var field = "services." + slug;
            found[slug] = new ProductServiceIdentityValueObject
            {
                Service = slug,
                Name = name is null ? null : Text(name, field + ".name", ProductIdentityRules.MaxNameLength),
                Description = description is null
                    ? null
                    : Text(description, field + ".description", ProductIdentityRules.MaxDescriptionLength),
            };
        }

        return [.. found.Values];
    }

    /// <summary>Requires a name or description: a scalar that keeps the identity rules.</summary>
    private static string Text(YamlNode node, string field, int maxLength)
    {
        var text = Scalar(node, field);
        return ProductIdentityRules.Text(text, maxLength) is { } rule ? throw YamlReader.Refuse(node, field, rule) : text;
    }

    /// <summary>Requires a homepage: an http or https address without credentials.</summary>
    private static string Homepage(YamlNode node)
    {
        var text = Scalar(node, "homepage");
        return ProductIdentityRules.Homepage(text) is { } rule ? throw YamlReader.Refuse(node, "homepage", rule) : text;
    }

    private static string Scalar(YamlNode node, string field) =>
        node is YamlScalarNode { Value: { } value } ? value : throw YamlReader.Refuse(node, field, "must be text");
}
