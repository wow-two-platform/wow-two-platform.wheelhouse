using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Models;
using Wheelhouse.Domain.Targets.Entities;

namespace Wheelhouse.Application.Products.Mappers;

/// <summary>Maps a stored product and its targets to the product integrations read, and a release source both ways.</summary>
internal static class ProductMapper
{
    /// <summary>Maps a product, its targets, each server's vault and the published sites to the product's projection.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="targets">The product's targets.</param>
    /// <param name="vaults">Each server's vault slug, by server identifier.</param>
    /// <param name="sites">Each target's published sites, by target slug.</param>
    /// <returns>The product integrations read.</returns>
    public static ProductDto Map(
        ProductEntity product,
        IEnumerable<TargetEntity> targets,
        IReadOnlyDictionary<Guid, string> vaults,
        IReadOnlyDictionary<string, IReadOnlyList<ProductSiteModel>> sites) => new()
    {
        Slug = product.Slug,
        Name = product.Name,
        Description = product.Description,
        Lifecycle = product.Lifecycle,
        Repository = new ProductRepositoryDto
        {
            Name = product.Repository,
            Url = "https://github.com/" + product.Repository,
            DefaultBranch = product.DefaultBranch,
        },
        Release = product.Release is { } release ? Map(release) : null,
        IconUrl = "/api/products/" + product.Slug + "/icon",
        Environments =
        [
            .. targets
                .OrderBy(target => target.Environment)
                .ThenBy(target => target.Slug, StringComparer.Ordinal)
                .Select(target => Map(product, target, vaults, sites)),
        ],
    };

    /// <summary>Maps a release source to its projection.</summary>
    /// <param name="release">The stored release source.</param>
    /// <returns>The release source integrations read.</returns>
    public static ProductReleaseDto Map(ProductReleaseValueObject release) => new()
    {
        Asset = release.Asset,
        Workflow = release.Workflow,
        Images = [.. release.Images.Select(image => new ReleaseImageDto { Service = image.Service, Image = image.Image })],
    };

    /// <summary>Maps one target to the environment integrations read, leaving the target and server behind.</summary>
    /// <param name="product">The target's product.</param>
    /// <param name="target">The stored target.</param>
    /// <param name="vaults">Each server's vault slug, by server identifier.</param>
    /// <param name="sites">Each target's published sites, by target slug.</param>
    /// <returns>The environment integrations read.</returns>
    private static ProductEnvironmentDto Map(
        ProductEntity product,
        TargetEntity target,
        IReadOnlyDictionary<Guid, string> vaults,
        IReadOnlyDictionary<string, IReadOnlyList<ProductSiteModel>> sites)
    {
        var environment = target.Environment.ToString().ToLowerInvariant();
        return new ProductEnvironmentDto
        {
            Name = environment,
            Sites =
            [
                .. sites.GetValueOrDefault(target.Slug, [])
                    .Select(site => new ProductSiteDto { Name = site.Name, Url = site.Url, Exposure = site.Exposure }),
            ],
            // One namespace per product environment, on the vault of the server the environment runs on.
            Secrets = vaults.TryGetValue(target.ServerId, out var vault)
                ? new ProductSecretsDto { Vault = vault, Namespace = product.Slug + "-" + environment }
                : null,
        };
    }
}
