using System.Text.RegularExpressions;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Parsers;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Domain.Products.Models;
using WoW.Two.Sdk.Beta.Be.Integrations.GitHub;

namespace Wheelhouse.Application.Catalog.Services;

/// <summary>Derives the release source of a product created from the catalog, so the owner types none: the asset
/// <c>{slug}-release.tar.gz</c>, the workflow <c>publish-docker-image.yml</c>, and one image per service key in the
/// primary repository's <c>engineering/deployment/deploy.yml</c> at the release branch's head — the service's own
/// <c>image</c>, else <c>ghcr.io/{owner/repo, lowercase}/{service}</c>, the shared publish workflow's convention. When
/// the descriptor cannot be read or parsed, the repository gets no images and the note says what follows: a product's
/// only repository leaves the product without images; a further repository of a product built from several adds no
/// services until it has a usable descriptor and releases, since the product's release sets leave it out until then;
/// and the primary one, which serves the front door, holds back every release set (decision 70). The same derivation runs
/// again whenever a product's approved services follow its repositories (<c>ProductReleaseSourcesService</c>).</summary>
public sealed class CatalogReleaseService(IGitHubRepositoryReader files)
{
    /// <summary>Derives a product's release source.</summary>
    /// <param name="product">The product's slug.</param>
    /// <param name="repository">The primary repository, as <c>owner/name</c>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The release source and, when its images are missing, why.</returns>
    public Task<DerivedRelease> DeriveAsync(string product, string repository, CancellationToken ct) =>
        DeriveAsync(product, repository, Role.Only, CatalogConstants.ReleaseBranch, fresh: true, ct);

    /// <summary>Derives the release source of every repository of a product, primary first. Each repository of a
    /// product built from several (decision 70) publishes the same asset, since its <c>deploy.yml</c> names the
    /// product's slug.</summary>
    /// <param name="product">The product's slug.</param>
    /// <param name="repositories">The repositories, as <c>owner/name</c>, primary first.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each repository's release source and note, in order.</returns>
    public Task<IReadOnlyList<(string Repository, DerivedRelease Derived)>> DeriveAllAsync(
        string product, IReadOnlyList<string> repositories, CancellationToken ct) =>
        DeriveAllAsync(product, repositories, CatalogConstants.ReleaseBranch, fresh: true, ct);

    /// <summary>Derives the release source of every repository of a product at a branch's head, primary first.</summary>
    /// <param name="product">The product's slug.</param>
    /// <param name="repositories">The repositories, as <c>owner/name</c>, primary first.</param>
    /// <param name="branch">The branch whose head's <c>deploy.yml</c> is read.</param>
    /// <param name="fresh">Whether to ask GitHub for the head now rather than take one read a few minutes ago.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each repository's release source and note, in order.</returns>
    public async Task<IReadOnlyList<(string Repository, DerivedRelease Derived)>> DeriveAllAsync(
        string product, IReadOnlyList<string> repositories, string branch, bool fresh, CancellationToken ct)
    {
        var result = new List<(string, DerivedRelease)>();
        for (var index = 0; index < repositories.Count; index++)
        {
            var role = repositories.Count == 1 ? Role.Only : index == 0 ? Role.Primary : Role.Further;
            result.Add((repositories[index], await DeriveAsync(product, repositories[index], role, branch, fresh, ct)));
        }

        return result;
    }

    private async Task<DerivedRelease> DeriveAsync(
        string product, string repository, Role role, string branch, bool fresh, CancellationToken ct)
    {
        var where = $"{repository}@{branch}";
        var commit = await files.HeadAsync(repository, branch, fresh, ct);
        if (commit is null)
            return Imageless(product, $"{where} could not be read from GitHub{Outcome(role, "it can be read")}");

        var file = await files.ReadAsync(repository, commit, CatalogConstants.DescriptorPath, CatalogConstants.MaxDescriptorBytes, ct);
        switch (file.Outcome)
        {
            case FileRead.NotFound:
                return Imageless(product, role == Role.Only
                    ? $"{where} has no {CatalogConstants.DescriptorPath}{Outcome(role, "")}"
                    : $"{where} has no {CatalogConstants.DescriptorPath} yet{Outcome(role, "it has one")}");
            case FileRead.TooLarge:
                return Imageless(product, $"{CatalogConstants.DescriptorPath} in {where} is too large to read{Outcome(role, "it can be read")}");
            case not FileRead.Found:
                return Imageless(product, $"{CatalogConstants.DescriptorPath} in {where} could not be read{Outcome(role, "it can be read")}");
        }

        var parsed = DeployDescriptorParser.Parse(file.Content!);
        if (parsed.Value is not { } descriptor)
            return Imageless(product, role == Role.Only
                ? $"deploy.yml in {where} {parsed.Problem} The product has no images yet."
                : $"deploy.yml in {where} {parsed.Problem} Until it is fixed{Outcome(role, null)}");

        var images = descriptor.Services
            .Select(service => new ReleaseImageValueObject
            {
                Service = service.Service, Image = service.Image ?? CatalogConstants.Image(repository, service.Service),
            })
            .ToList();
        if (images.FirstOrDefault(image => !Regex.IsMatch(image.Image, InventoryPatternConstants.Image)) is { } invalid)
            return Imageless(product, $"deploy.yml in {where} gives service '{invalid.Service}' an image Wheelhouse cannot use{Outcome(role, "it is fixed")}");

        var note = descriptor.Product == product
            ? null
            : role != Role.Only
                ? $"deploy.yml in {where} names the product '{descriptor.Product}', not '{product}'; every repository of '{product}' must name it before its releases join the product's."
                : $"deploy.yml in {where} names the product '{descriptor.Product}', not '{product}'; the release asset follows the catalog slug.";
        return new DerivedRelease(Release(product, images), note);
    }

    /// <summary>Ends a note on a repository that yields no images with what follows from it. A product's only repository
    /// leaves the product without images. A product built from several leaves a further repository out of its release
    /// sets until <paramref name="until"/> and it releases; its primary one serves the front door, so no set exists
    /// until then. With <paramref name="until"/> null the note already says what must change, and the ending reads on
    /// from "Until it is fixed".</summary>
    private static string Outcome(Role role, string? until) => (role, until) switch
    {
        (Role.Only, _) => ", so the product has no images yet.",
        (Role.Primary, null) => " and releases, the product has no release sets: it serves the product's front door.",
        (Role.Further, null) => " and releases, it adds no services.",
        (Role.Primary, _) => $"; it serves the product's front door, so the product has no release sets until {until} and releases.",
        _ => $", so it adds no services until {until} and releases.",
    };

    private static DerivedRelease Imageless(string product, string note) => new(Release(product, []), note, FromDescriptor: false);

    /// <summary>A repository's place in its product: its only one, or the primary or a further one of several.</summary>
    private enum Role
    {
        /// <summary>The product's only repository.</summary>
        Only,

        /// <summary>The primary repository of a product built from several: it serves the front door.</summary>
        Primary,

        /// <summary>A further repository of a product built from several.</summary>
        Further,
    }

    private static ProductReleaseValueObject Release(string product, IReadOnlyList<ReleaseImageValueObject> images) => new()
    {
        Asset = CatalogConstants.Asset(product),
        Workflow = CatalogConstants.Workflow,
        Images = images,
    };
}
