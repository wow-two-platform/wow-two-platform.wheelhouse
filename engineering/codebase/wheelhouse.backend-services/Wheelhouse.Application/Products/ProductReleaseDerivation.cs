using Wheelhouse.Application.Catalog;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Services;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Products;

/// <summary>Derives the release sources a product save stores, so the owner types none: each repository the save asks
/// to publish takes the asset and build workflow of the shared publish workflow and one image per service of its
/// <c>deploy.yml</c> at the branch head — the derivation the catalog import makes (<see cref="CatalogReleaseService"/>).
/// A value the save sends must be the derived one; anything else is refused, naming the value to leave out. While a
/// repository's descriptor cannot be read or used, its images are the ones sent, else the ones it had, as
/// <c>ProductReleaseSourcesService</c> keeps them; they follow the descriptor once it can be read.</summary>
public static class ProductReleaseDerivation
{
    /// <summary>Derives every repository's release source at a branch's head, primary first; a source that fails in a
    /// way it did not anticipate reads as a repository GitHub could not serve.</summary>
    /// <param name="releases">The catalog's release derivation.</param>
    /// <param name="product">The product's slug.</param>
    /// <param name="repositories">The repositories, as <c>owner/name</c>, primary first.</param>
    /// <param name="branch">The branch whose head's <c>deploy.yml</c> is read.</param>
    /// <param name="fresh">Whether to ask GitHub for the head now rather than take one read a few minutes ago.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each repository's derived release source and note, in order.</returns>
    public static async Task<IReadOnlyList<DerivedRelease>> DeriveAsync(
        CatalogReleaseService releases, string product, IReadOnlyList<string> repositories, string branch, bool fresh, CancellationToken ct)
    {
        try
        {
            return [.. (await releases.DeriveAllAsync(product, repositories, branch, fresh, ct)).Select(item => item.Derived)];
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return [.. repositories.Select(repository => new DerivedRelease(
                new ProductReleaseValueObject { Asset = CatalogConstants.Asset(product), Workflow = CatalogConstants.Workflow, Images = [] },
                $"{repository}@{branch} could not be read from GitHub; the images stay as they are until it can be.",
                FromDescriptor: false))];
        }
    }

    /// <summary>Resolves the release source of each repository a save leaves the product with, primary first.</summary>
    /// <param name="releases">The catalog's release derivation.</param>
    /// <param name="product">The product's slug.</param>
    /// <param name="branch">The product's branch.</param>
    /// <param name="repositories">Each repository with what the save asks of it.</param>
    /// <param name="stored">The repositories and release sources the product has now; empty for a new product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each repository's release source, or the refusal of a sent value that differs from the derived one.</returns>
    public static async Task<Resolution> ResolveAsync(
        CatalogReleaseService releases,
        string product,
        string branch,
        IReadOnlyList<Entry> repositories,
        IReadOnlyList<(string Repository, ProductReleaseValueObject? Release)> stored,
        CancellationToken ct)
    {
        var derived = repositories.Any(entry => entry.IsSet && entry.Requested is not null)
            ? await DeriveAsync(releases, product, [.. repositories.Select(entry => entry.Repository)], branch, fresh: false, ct)
            : null;
        var result = new List<(string Repository, ProductReleaseValueObject? Release)>();
        for (var index = 0; index < repositories.Count; index++)
        {
            var entry = repositories[index];
            if (!entry.IsSet || entry.Requested is null)
            {
                result.Add((entry.Repository, entry.IsSet ? null : entry.Kept));
                continue;
            }

            var source = derived![index];
            if (Refusal(product, branch, entry, source) is { } refusal)
                return new Resolution(result, refusal);

            var had = stored.FirstOrDefault(item => string.Equals(item.Repository, entry.Repository, StringComparison.OrdinalIgnoreCase)).Release;
            result.Add((entry.Repository, source.Release with
            {
                Images = source.FromDescriptor ? source.Release.Images : entry.Requested.Images ?? had?.Images ?? [],
            }));
        }

        return new Resolution(result, null);
    }

    /// <summary>Says which sent value differs from what the repository gives, or <c>null</c> when none does.</summary>
    private static string? Refusal(string product, string branch, Entry entry, DerivedRelease source)
    {
        var requested = entry.Requested!;
        if (!string.IsNullOrEmpty(requested.Asset) && requested.Asset != source.Release.Asset)
            return $"The release asset of {entry.Repository} is '{source.Release.Asset}', the shared publish workflow's name for "
                + $"'{product}'; leave it out and Wheelhouse fills it in.";
        if (!string.IsNullOrEmpty(requested.Workflow) && requested.Workflow != source.Release.Workflow)
            return $"The build workflow of {entry.Repository} is '{source.Release.Workflow}', the shared publish workflow; leave it "
                + "out and Wheelhouse fills it in.";
        if (source.FromDescriptor && requested.Images is { } images && !SameImages(images, source.Release.Images))
        {
            var services = source.Release.Images.Count == 0
                ? "no services"
                : string.Join(", ", source.Release.Images.Select(image => $"{image.Service} → {image.Image}"));
            return $"deploy.yml in {entry.Repository}@{branch} names {services}; leave the images out and Wheelhouse takes them from it.";
        }

        return null;
    }

    private static bool SameImages(IReadOnlyList<ReleaseImageValueObject> sent, IReadOnlyList<ReleaseImageValueObject> derived) =>
        sent.Count == derived.Count
        && sent.All(image => derived.Any(item =>
            item.Service == image.Service && string.Equals(item.Image, image.Image, StringComparison.OrdinalIgnoreCase)));

    /// <summary>One repository of a save: what it asks the repository to publish, or the release source it keeps.</summary>
    /// <param name="Repository">The repository, as <c>owner/name</c>.</param>
    /// <param name="IsSet">Whether the save sets this repository's release source; otherwise it keeps <paramref name="Kept"/>.</param>
    /// <param name="Requested">What the save asks of the repository, or <c>null</c> for no release source.</param>
    /// <param name="Kept">The release source a repository the save leaves alone keeps.</param>
    public sealed record Entry(string Repository, bool IsSet, ProductReleaseRequest? Requested, ProductReleaseValueObject? Kept = null);

    /// <summary>What a save's release sources resolved to.</summary>
    /// <param name="Repositories">Each repository and its release source, primary first; partial when refused.</param>
    /// <param name="Refusal">Why a sent value was refused, or <c>null</c>.</param>
    public sealed record Resolution(IReadOnlyList<(string Repository, ProductReleaseValueObject? Release)> Repositories, string? Refusal);
}
