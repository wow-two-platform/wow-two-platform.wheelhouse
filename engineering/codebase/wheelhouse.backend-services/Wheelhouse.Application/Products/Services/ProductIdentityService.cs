using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Catalog;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Products.Services;

/// <summary>Decides what each product is called and described as: the cached identity its repository declares when it
/// declares a valid one, otherwise the product's own row — and keeps that cache current. A product built from several
/// repositories always reads as its own row.</summary>
/// <remarks>
/// The repository declares its identity in <c>engineering/deployment/deploy.yml</c> (decision 7); while the descriptor
/// declares none, a retired <c>product/product.json</c> still applies, with a notice to move it. The cache is a
/// projection with one row per product holding the identity, the commit and the file it was read from. Nothing expires
/// it on a timer: a row is replaced only by a copy read at a newer commit, and dropped when a newer commit declares no
/// identity in either file. On a read, the product's branch head is compared with the row's commit — the source keeps a
/// head for a few minutes, so GitHub is asked at most that often — and a different head reads the files at that commit.
/// An invalid or oversized file is ignored with a reason and the previous valid row stays; a repository that cannot be
/// read changes nothing. An explicit refresh asks for the head again now.
/// </remarks>
public sealed class ProductIdentityService(IProductManifestsRepository manifests, IRepositoryFileSource files)
{
    /// <summary>The file a product declares its identity in.</summary>
    public const string DescriptorFile = CatalogConstants.DescriptorPath;

    /// <summary>The retired identity file, read only while the descriptor declares no identity.</summary>
    public const string RetiredFile = ProductManifestParser.Path;

    /// <summary>What the console tells an owner whose identity still comes from the retired file.</summary>
    public const string RetiredNotice =
        "The name and description come from product/product.json, which is retired: move them into " +
        "engineering/deployment/deploy.yml (name, description, homepage) and delete the file.";

    /// <summary>Resolves the identity of some products, refreshing any whose branch head moved.</summary>
    /// <param name="products">The stored products.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Each product's identity, keyed by product identifier.</returns>
    public async Task<IReadOnlyDictionary<Guid, ProductIdentity>> ResolveAsync(
        IReadOnlyList<ProductEntity> products, CancellationToken ct)
    {
        if (products.Count == 0)
            return new Dictionary<Guid, ProductIdentity>();

        var stored = await manifests.GetByProductsAsync([.. products.Select(product => product.Id)], ct);
        var outcomes = await Task.WhenAll(
            products.Select(product => CheckAsync(product, stored.GetValueOrDefault(product.Id), fresh: false, ct)));

        // The reads above overlap; the store is written one product at a time.
        var identities = new Dictionary<Guid, ProductIdentity>();
        foreach (var outcome in outcomes)
            identities[outcome.Product.Id] = await ApplyAsync(outcome, ct);
        return identities;
    }

    /// <summary>Reads the product's branch head again now, bypassing any remembered one, and re-reads the identity when
    /// the head differs from the cached commit.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The product's identity, or <c>null</c> when the repository's branch could not be read.</returns>
    public async Task<ProductIdentity?> RefreshAsync(ProductEntity product, CancellationToken ct)
    {
        var stored = (await manifests.GetByProductsAsync([product.Id], ct)).GetValueOrDefault(product.Id);
        var outcome = await CheckAsync(product, stored, fresh: true, ct);
        var identity = await ApplyAsync(outcome, ct);
        return outcome.Reachable ? identity : null;
    }

    private async Task<Outcome> CheckAsync(ProductEntity product, ProductManifestEntity? stored, bool fresh, CancellationToken ct)
    {
        // A copy read from another repository or branch than the product names now is not the product's.
        var kept = stored is not null
            && string.Equals(stored.Repository, product.Repository, StringComparison.OrdinalIgnoreCase)
            && string.Equals(stored.Branch, product.DefaultBranch, StringComparison.Ordinal)
                ? stored
                : null;
        var outcome = new Outcome { Product = product, Stored = stored, Kept = kept };
        // A product built from several repositories is named by its own row (its catalog entry): each repository's
        // identity describes that repository, not the product (decision 70).
        if (product.Components.Count > 0)
            return outcome with { Kept = null };

        try
        {
            var head = await files.HeadAsync(product.Repository, product.DefaultBranch, fresh, ct);
            if (head is null)
                return outcome with { Reachable = false };
            if (kept is not null && string.Equals(kept.Commit, head, StringComparison.Ordinal))
                return outcome;

            var descriptor = await files.ReadAsync(
                product.Repository, head, DescriptorFile, CatalogConstants.MaxDescriptorBytes, ct);
            switch (descriptor.Status)
            {
                case RepositoryFileStatus.Unreadable:
                    return outcome with { Reachable = false };
                case RepositoryFileStatus.TooLarge:
                    return outcome with { Problem = DescriptorIgnored("The file is larger than 256 KiB.") };
                case RepositoryFileStatus.Found:
                    var verdict = DescriptorIdentityParser.Parse(descriptor.Content ?? []);
                    if (!verdict.IsAbsent)
                        return Judge(outcome, head, DescriptorFile, verdict, DescriptorIgnored);
                    break;
            }

            // The descriptor declares no identity: a retired product/product.json still applies, with a notice.
            var retired = await files.ReadAsync(product.Repository, head, RetiredFile, ProductManifestParser.MaxBytes, ct);
            return retired.Status switch
            {
                RepositoryFileStatus.Found => Judge(
                    outcome, head, RetiredFile, ProductManifestParser.Parse(retired.Content ?? []), RetiredIgnored),
                RepositoryFileStatus.Missing => outcome with { Kept = null },
                RepositoryFileStatus.TooLarge => outcome with { Problem = RetiredIgnored("The file is larger than 16 KiB.") },
                _ => outcome with { Reachable = false },
            };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // A source that fails in a way it did not anticipate must not fail the read; the cached copy stands.
            return outcome with { Reachable = false };
        }
    }

    private static string DescriptorIgnored(string reason) => $"The identity in {DescriptorFile} is ignored: {reason}";

    private static string RetiredIgnored(string reason) => $"{RetiredFile} is ignored: {reason}";

    private static Outcome Judge(
        Outcome outcome, string head, string file, ProductManifestParse verdict, Func<string, string> ignored)
    {
        if (verdict.Manifest is not { } manifest)
            return outcome with { Problem = ignored(verdict.Problem ?? "It declares no identity.") };

        var replacement = new ProductManifestEntity
        {
            ProductId = outcome.Product.Id,
            Repository = outcome.Product.Repository,
            Branch = outcome.Product.DefaultBranch,
            Commit = head,
            SourceFile = file,
            Name = manifest.Name,
            Description = manifest.Description,
            Homepage = manifest.Homepage,
            Services = manifest.Services,
        };
        return outcome with { Kept = replacement, Replacement = replacement };
    }

    private async Task<ProductIdentity> ApplyAsync(Outcome outcome, CancellationToken ct)
    {
        if (outcome.Replacement is not null)
            await manifests.SaveAsync(outcome.Replacement, ct);
        else if (outcome.Stored is not null && outcome.Kept is null)
            await manifests.DeleteAsync(outcome.Product.Id, ct);

        return outcome.Kept is { } manifest
            ? new ProductIdentity(
                manifest.Name, manifest.Description, manifest.Homepage, ProductIdentitySource.Manifest,
                manifest.Commit, outcome.Problem, manifest.Services, manifest.SourceFile,
                manifest.SourceFile == RetiredFile ? RetiredNotice : null)
            : new ProductIdentity(
                outcome.Product.Name, outcome.Product.Description, null, ProductIdentitySource.Record,
                null, outcome.Problem, []);
    }

    /// <summary>What checking one product found, before the store is touched.</summary>
    private sealed record Outcome
    {
        /// <summary>Gets the product checked.</summary>
        public required ProductEntity Product { get; init; }

        /// <summary>Gets the row as stored, even when it was read from another repository or branch.</summary>
        public ProductManifestEntity? Stored { get; init; }

        /// <summary>Gets the row that still applies to the product after the check.</summary>
        public ProductManifestEntity? Kept { get; init; }

        /// <summary>Gets the row to store in place of the stored one.</summary>
        public ProductManifestEntity? Replacement { get; init; }

        /// <summary>Gets whether the repository's branch could be read.</summary>
        public bool Reachable { get; init; } = true;

        /// <summary>Gets why the repository's newest identity is ignored, as a sentence naming the file.</summary>
        public string? Problem { get; init; }
    }
}
