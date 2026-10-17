using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Repositories;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the portfolio's products.</summary>
internal sealed class ProductsRepository(WheelhouseDbContext db) : EfRepository<ProductEntity, Guid>(db), IProductsRepository
{
    /// <inheritdoc />
    public override async Task<IReadOnlyList<ProductEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().OrderBy(entity => entity.Slug).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ProductEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(entity => entity.Slug == slug, ct);

    /// <inheritdoc />
    public Task<ProductEntity?> FindAsync(string key, CancellationToken ct = default) =>
        Guid.TryParse(key, out var id)
            ? Set.FirstOrDefaultAsync(entity => entity.Id == id, ct)
            : GetBySlugAsync(key, ct);

    /// <inheritdoc />
    public Task<bool> AnyInWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        Set.AnyAsync(entity => entity.WorkspaceId == workspaceId, ct);
}
