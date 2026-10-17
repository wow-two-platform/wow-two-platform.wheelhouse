namespace Wheelhouse.Application.Catalog;

/// <summary>Holds the conventions a product created from the catalog follows: the shared publish workflow's release
/// asset, workflow and image repositories, and where its deployment descriptor lives.</summary>
public static class CatalogConstants
{
    /// <summary>Holds the largest catalog file read.</summary>
    public const int MaxCatalogBytes = 512 * 1024;

    /// <summary>Holds the largest <c>deploy.yml</c> read.</summary>
    public const int MaxDescriptorBytes = 256 * 1024;

    /// <summary>Holds where a product's deployment descriptor lives in its repository.</summary>
    public const string DescriptorPath = "engineering/deployment/deploy.yml";

    /// <summary>Holds the branch a product releases from; the shared publish workflow releases <c>main</c>.</summary>
    public const string ReleaseBranch = "main";

    /// <summary>Holds the workflow every product builds through, which calls the shared publish workflow.</summary>
    public const string Workflow = "publish-docker-image.yml";

    /// <summary>Holds the registry the shared publish workflow pushes to: <c>ghcr.io/{owner/repo, lowercase}/{service}</c>
    /// unless a service names its own <c>image</c>.</summary>
    public const string Registry = "ghcr.io";

    /// <summary>Holds the longest product description the inventory keeps.</summary>
    public const int DescriptionLength = 200;

    /// <summary>Holds the longest product name the inventory keeps.</summary>
    public const int NameLength = 80;

    /// <summary>Gets the release asset a product's published releases carry.</summary>
    /// <param name="product">The product's slug.</param>
    /// <returns>The asset's file name, such as <c>foreverpin-release.tar.gz</c>.</returns>
    public static string Asset(string product) => product + "-release.tar.gz";

    /// <summary>Gets the image repository the shared publish workflow pushes a service to.</summary>
    /// <param name="repository">The product's primary repository, as <c>owner/name</c>.</param>
    /// <param name="service">The service's key in <c>deploy.yml</c>.</param>
    /// <returns>The image repository, such as <c>ghcr.io/sulton-max/10x-venture-forever-pin/management</c>.</returns>
    public static string Image(string repository, string service) =>
        Registry + "/" + repository.ToLowerInvariant() + "/" + service;
}
