using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Uniqueness.Extensions;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IProductManifestsRepository"/>. A row is never left tracked, so a later
/// write to the same product in the same scope starts clean.</summary>
internal sealed class EfProductManifests(WheelhouseDbContext db) : IProductManifestsRepository
{
    /// <summary>Holds the key every product's one row is upserted on: the table's primary key, <c>product_id</c>.</summary>
    private const string ProductKeyConstraint = "product_manifests_pkey";

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, ProductManifestEntity>> GetByProductsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default) =>
        await db.ProductManifests.AsNoTracking()
            .Where(manifest => productIds.Contains(manifest.ProductId))
            .ToDictionaryAsync(manifest => manifest.ProductId, ct);

    /// <inheritdoc />
    public async Task SaveAsync(ProductManifestEntity manifest, CancellationToken ct = default)
    {
        var row = manifest;
        try
        {
            // The SDK upsert inserts the row, or updates it, also when another request inserted it first.
            var saved = await db.UpsertAsync(
                stored => stored.ProductId == manifest.ProductId,
                manifest,
                stored =>
                {
                    stored.Repository = manifest.Repository;
                    stored.Branch = manifest.Branch;
                    stored.Commit = manifest.Commit;
                    stored.Name = manifest.Name;
                    stored.Description = manifest.Description;
                    stored.Homepage = manifest.Homepage;
                    stored.Services = manifest.Services;
                },
                ProductKeyConstraint,
                ct);
            row = saved.Entity;
        }
        catch (DbUpdateException)
        {
            // The product is gone; the row is a cache, so the read shows what it already has and the next read settles it.
        }
        finally
        {
            db.Entry(manifest).State = EntityState.Detached;
            db.Entry(row).State = EntityState.Detached;
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid productId, CancellationToken ct = default) =>
        db.ProductManifests.Where(manifest => manifest.ProductId == productId).ExecuteDeleteAsync(ct);
}
