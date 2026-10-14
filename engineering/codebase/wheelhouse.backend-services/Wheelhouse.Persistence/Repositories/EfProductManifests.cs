using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IProductManifestsRepository"/>. A row is never left tracked, so a later
/// write to the same product in the same scope starts clean.</summary>
internal sealed class EfProductManifests(WheelhouseDbContext db) : IProductManifestsRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, ProductManifestEntity>> GetByProductsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default) =>
        await db.ProductManifests.AsNoTracking()
            .Where(manifest => productIds.Contains(manifest.ProductId))
            .ToDictionaryAsync(manifest => manifest.ProductId, ct);

    /// <inheritdoc />
    public async Task SaveAsync(ProductManifestEntity manifest, CancellationToken ct = default)
    {
        var existing = await db.ProductManifests.FirstOrDefaultAsync(row => row.ProductId == manifest.ProductId, ct);
        var row = existing ?? manifest;
        if (existing is null)
            db.ProductManifests.Add(manifest);
        else
        {
            existing.Repository = manifest.Repository;
            existing.Branch = manifest.Branch;
            existing.Commit = manifest.Commit;
            existing.Name = manifest.Name;
            existing.Description = manifest.Description;
            existing.Homepage = manifest.Homepage;
            existing.Services = manifest.Services;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request wrote this product's row first (or the product is gone); the row is a cache, so the
            // read that lost the race shows what it already has and the next read settles it.
        }
        finally
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid productId, CancellationToken ct = default) =>
        db.ProductManifests.Where(manifest => manifest.ProductId == productId).ExecuteDeleteAsync(ct);
}
