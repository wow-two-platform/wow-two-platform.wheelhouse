using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Catalog.Services;
using Wheelhouse.Domain.Audit.Enums;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Models;

namespace Wheelhouse.Application.Products.Services;

/// <summary>Keeps each product's approved services following its repositories' <c>deploy.yml</c>. Every repository that
/// has a release source takes the services its descriptor names at the branch head, each with the image the service
/// names, else <c>ghcr.io/{owner/repo}/{service}</c> — the derivation the catalog import made once
/// (<see cref="CatalogReleaseService"/>). A service a repository adds, and the services of a repository whose first
/// <c>deploy.yml</c> came after the import, so reach the runner's release sets without a re-import, and a removed one
/// leaves. A repository whose descriptor cannot be read or used keeps what it had, and so does a product whose
/// repositories would then declare one service twice. A change is saved, exported to the runner's snapshot and audited
/// as <c>product.services</c>, naming what was added (<c>+</c>), removed (<c>-</c>) or moved to another image
/// (<c>~</c>).</summary>
/// <remarks>It runs when the owner refreshes a product, on each automation pass (the release watcher), when the release
/// catalog is listed and before the runner prepares a release for a target. The head comes from the file source's
/// few-minute cache unless a refresh asks for it now, and each commit's descriptor is read once, so a run costs GitHub
/// little. The runner checks every release against its own services (<c>artifacts.py</c> <c>validate_source</c>), so a
/// release older or newer than what this approved still deploys while its images stay in the approved places.</remarks>
public sealed partial class ProductReleaseSourcesService(
    IProductsRepository products,
    ITargetsRepository targets,
    CatalogReleaseService releases,
    IInventorySnapshot snapshot,
    IAuditTrail audit,
    IActorContext actor,
    ILogger<ProductReleaseSourcesService> logger)
{
    /// <summary>Holds the audit action a change of a product's approved services records.</summary>
    public const string AuditAction = "product.services";

    /// <summary>Holds the turn every comparison and write in this process waits for, so two runs that read the same
    /// change write and audit it once.</summary>
    private static readonly SemaphoreSlim Turn = new(1, 1);

    /// <summary>Brings every product's approved services up to its repositories' descriptors.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The slugs of the products whose services changed.</returns>
    public Task<IReadOnlyList<string>> SyncAllAsync(CancellationToken ct) => RunAsync(null, fresh: false, ct);

    /// <summary>Brings one product's approved services up to its repositories' descriptors.</summary>
    /// <param name="product">The product's slug.</param>
    /// <param name="fresh">Whether to ask GitHub for each branch head now, as an explicit refresh does.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>true</c> when the product's services changed.</returns>
    public async Task<bool> SyncProductAsync(string product, bool fresh, CancellationToken ct) =>
        (await RunAsync(product, fresh, ct)).Count > 0;

    /// <summary>Brings the approved services of a target's product up to its repositories' descriptors; a target the
    /// inventory lacks changes nothing.</summary>
    /// <param name="target">The target's slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the product is current.</returns>
    public async Task SyncTargetAsync(string target, CancellationToken ct)
    {
        if (await targets.GetBySlugAsync(target, ct) is { } row && await products.GetByIdAsync(row.ProductId, ct) is { } product)
            await SyncProductAsync(product.Slug, fresh: false, ct);
    }

    private async Task<IReadOnlyList<string>> RunAsync(string? only, bool fresh, CancellationToken ct)
    {
        var rows = (await products.GetAllAsync(ct))
            .Where(row => (only is null || row.Slug == only) && ProductRepositories.Of(row).Any(item => item.Release is not null))
            .ToList();
        if (rows.Count == 0)
            return [];

        // GitHub is read outside the turn, every product at once; only the comparison and the write take turns.
        var reads = await Task.WhenAll(rows.Select(row => ReadAsync(row, fresh, ct)));
        var changed = new List<string>();
        await Turn.WaitAsync(ct);
        try
        {
            var current = (await products.GetAllAsync(ct)).ToDictionary(row => row.Slug, StringComparer.Ordinal);
            foreach (var read in reads.OfType<Read>())
            {
                if (await ApplyAsync(read, current.GetValueOrDefault(read.Product), ct))
                    changed.Add(read.Product);
            }
        }
        finally
        {
            Turn.Release();
        }

        if (changed.Count > 0)
            await snapshot.ExportAsync(CancellationToken.None);
        return changed;
    }

    /// <summary>Reads what each of a product's repositories' descriptors names now; <c>null</c> for a repository without
    /// a release source or whose descriptor could not be used, and for the whole product when reading failed.</summary>
    private async Task<Read?> ReadAsync(ProductEntity product, bool fresh, CancellationToken ct)
    {
        var stored = ProductRepositories.Of(product);
        var repositories = stored.Select(item => item.Repository).ToList();
        try
        {
            var derived = await releases.DeriveAllAsync(product.Slug, repositories, product.DefaultBranch, fresh, ct);
            return new Read(product.Slug, repositories,
            [
                .. derived.Select((item, index) => stored[index].Release is not null && item.Derived.FromDescriptor
                    ? item.Derived.Release.Images
                    : null),
            ]);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            // A source that fails in a way it did not anticipate leaves the product as it is; the next run reads again.
            LogUnread(logger, product.Slug, exception.GetType().Name);
            return null;
        }
    }

    /// <summary>Saves, exports and audits what a read changed of a product as it is stored now. Says whether it did.</summary>
    private async Task<bool> ApplyAsync(Read read, ProductEntity? stored, CancellationToken ct)
    {
        // A product removed, or given other repositories, since the read is left for the next run.
        if (stored is null)
            return false;
        var before = ProductRepositories.Of(stored);
        if (!before.Select(item => item.Repository).SequenceEqual(read.Repositories, StringComparer.OrdinalIgnoreCase))
            return false;

        var after = before
            .Select((item, index) => item.Release is { } release && read.Images[index] is { } images
                ? (item.Repository, Release: (ProductReleaseValueObject?)(release with { Images = images }))
                : item)
            .ToList();
        if (Changes(stored.Slug, before, after) is not { } detail)
            return false;
        if (ProductRepositories.Problem(stored.Slug, after) is { } problem)
        {
            LogKept(logger, stored.Slug, problem);
            return false;
        }

        // The tracked row: the one a request already holds, so what it answers with shows the change.
        var tracked = await products.GetBySlugAsync(stored.Slug, ct);
        if (tracked is null)
            return false;
        tracked.Release = after[0].Release;
        tracked.Components = [.. after.Skip(1).Select(item => new ProductComponentValueObject { Repository = item.Repository, Release = item.Release })];
        await products.UpdateAsync(tracked, ct);
        LogFollowed(logger, stored.Slug, detail);
        try
        {
            // The change has happened; a caller that disconnects must not cancel its record.
            await audit.AppendAsync(new AuditRecord(actor.Actor, AuditAction, stored.Slug, AuditOutcome.Succeeded, detail, null),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogUnaudited(logger, stored.Slug, exception.GetType().Name);
        }

        return true;
    }

    /// <summary>Names each repository's added (<c>+</c>), removed (<c>-</c>) and re-imaged (<c>~</c>) services, the
    /// repository by its short name in a product of several; <c>null</c> when nothing but their order changed.</summary>
    private static string? Changes(
        string product,
        IReadOnlyList<(string Repository, ProductReleaseValueObject? Release)> before,
        IReadOnlyList<(string Repository, ProductReleaseValueObject? Release)> after)
    {
        var parts = new List<string>();
        for (var index = 0; index < before.Count; index++)
        {
            var old = Images(before[index].Release);
            var now = Images(after[index].Release);
            var changes = now.Keys.Where(service => !old.ContainsKey(service)).Select(service => "+" + service)
                .Concat(old.Keys.Where(service => !now.ContainsKey(service)).Select(service => "-" + service))
                .Concat(now.Where(pair => old.TryGetValue(pair.Key, out var image) && image != pair.Value).Select(pair => "~" + pair.Key))
                .ToList();
            if (changes.Count > 0)
                parts.Add((before.Count > 1 ? ProductRepositories.ShortName(product, before[index].Repository) + " " : "")
                    + string.Join(" ", changes));
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static Dictionary<string, string> Images(ProductReleaseValueObject? release)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var image in release?.Images ?? [])
            result.TryAdd(image.Service, image.Image);
        return result;
    }

    /// <summary>What one product's repositories' descriptors named: each repository's images, or <c>null</c> where
    /// they stay as stored.</summary>
    private sealed record Read(
        string Product, IReadOnlyList<string> Repositories, IReadOnlyList<IReadOnlyList<ReleaseImageValueObject>?> Images);

    [LoggerMessage(Level = LogLevel.Information, Message = "Product {Product} now follows its deploy.yml services: {Changes}")]
    private static partial void LogFollowed(ILogger logger, string product, string changes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Product {Product} keeps its approved services: {Problem}")]
    private static partial void LogKept(ILogger logger, string product, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Product {Product}'s deploy.yml services could not be read ({Reason}); the next run reads them again")]
    private static partial void LogUnread(ILogger logger, string product, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit trail append failed for a services change of {Product} ({Reason})")]
    private static partial void LogUnaudited(ILogger logger, string product, string reason);
}
