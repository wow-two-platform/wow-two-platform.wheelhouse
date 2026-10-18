using System.Text.RegularExpressions;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Parsers;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Catalog.Services;

/// <summary>Derives the release source of a product created from the catalog, so the owner types none: the asset
/// <c>{slug}-release.tar.gz</c>, the workflow <c>publish-docker-image.yml</c>, and one image per service key in the
/// primary repository's <c>engineering/deployment/deploy.yml</c> at the release branch's head — the service's own
/// <c>image</c>, else <c>ghcr.io/{owner/repo, lowercase}/{service}</c>, the shared publish workflow's convention. When
/// the descriptor cannot be read or parsed, the product gets no images and the note says why.</summary>
public sealed class CatalogReleaseService(IRepositoryFileSource files)
{
    /// <summary>Derives a product's release source.</summary>
    /// <param name="product">The product's slug.</param>
    /// <param name="repository">The primary repository, as <c>owner/name</c>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The release source and, when its images are missing, why.</returns>
    public async Task<DerivedRelease> DeriveAsync(string product, string repository, CancellationToken ct)
    {
        var where = $"{repository}@{CatalogConstants.ReleaseBranch}";
        var commit = await files.HeadAsync(repository, CatalogConstants.ReleaseBranch, fresh: true, ct);
        if (commit is null)
            return Imageless(product, $"{where} could not be read from GitHub, so the product has no images yet.");

        var file = await files.ReadAsync(repository, commit, CatalogConstants.DescriptorPath, CatalogConstants.MaxDescriptorBytes, ct);
        switch (file.Status)
        {
            case RepositoryFileStatus.Missing:
                return Imageless(product, $"{where} has no {CatalogConstants.DescriptorPath}, so the product has no images yet.");
            case RepositoryFileStatus.TooLarge:
                return Imageless(product, $"{CatalogConstants.DescriptorPath} in {where} is too large to read, so the product has no images yet.");
            case RepositoryFileStatus.Unreadable:
                return Imageless(product, $"{CatalogConstants.DescriptorPath} in {where} could not be read, so the product has no images yet.");
        }

        var parsed = DeployDescriptorParser.Parse(file.Content!);
        if (parsed.Value is not { } descriptor)
            return Imageless(product, $"deploy.yml in {where} {parsed.Problem} The product has no images yet.");

        var images = descriptor.Services
            .Select(service => new ReleaseImageValueObject
            {
                Service = service.Service, Image = service.Image ?? CatalogConstants.Image(repository, service.Service),
            })
            .ToList();
        if (images.FirstOrDefault(image => !Regex.IsMatch(image.Image, InventoryPatternConstants.Image)) is { } invalid)
            return Imageless(product, $"deploy.yml in {where} gives service '{invalid.Service}' an image Wheelhouse cannot use, so the product has no images yet.");

        var note = descriptor.Product == product
            ? null
            : $"deploy.yml in {where} names the product '{descriptor.Product}', not '{product}'; the release asset follows the catalog slug.";
        return new DerivedRelease(Release(product, images), note);
    }

    private static DerivedRelease Imageless(string product, string note) => new(Release(product, []), note);

    private static ProductReleaseValueObject Release(string product, IReadOnlyList<ReleaseImageValueObject> images) => new()
    {
        Asset = CatalogConstants.Asset(product),
        Workflow = CatalogConstants.Workflow,
        Images = images,
    };
}
