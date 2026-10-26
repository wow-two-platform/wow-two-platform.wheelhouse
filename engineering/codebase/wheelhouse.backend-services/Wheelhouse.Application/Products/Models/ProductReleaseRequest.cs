using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Products.Models;

/// <summary>Represents a request that a product's repository publish releases. Wheelhouse derives the release source from
/// the repository, as the catalog import does: the asset and the build workflow follow the shared publish workflow, and
/// the images follow the services of its <c>deploy.yml</c>. Every value may be left out; one that is sent must be the one
/// the repository gives, and is refused otherwise. While the descriptor cannot be read, sent images are kept.</summary>
public sealed record ProductReleaseRequest
{
    /// <summary>Gets the release asset, or <c>null</c> to take the derived one.</summary>
    public string? Asset { get; init; }

    /// <summary>Gets the build workflow, or <c>null</c> to take the derived one.</summary>
    public string? Workflow { get; init; }

    /// <summary>Gets each service's image repository, or <c>null</c> to take those the repository's descriptor names.</summary>
    public IReadOnlyList<ReleaseImageValueObject>? Images { get; init; }

    /// <summary>Represents a stored or fully sent release source as a request whose every value must hold.</summary>
    /// <param name="release">The release source.</param>
    /// <returns>The request, or <c>null</c> for none.</returns>
    public static ProductReleaseRequest? Of(ProductReleaseValueObject? release) =>
        release is null ? null : new ProductReleaseRequest { Asset = release.Asset, Workflow = release.Workflow, Images = release.Images };
}
