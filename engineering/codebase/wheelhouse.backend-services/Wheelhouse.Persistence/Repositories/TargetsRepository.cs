using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Repositories;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses each product's environments on their servers.</summary>
internal sealed class TargetsRepository(WheelhouseDbContext db) : EfRepository<TargetEntity, Guid>(db), ITargetsRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// By product slug, then along the promotion path in <see cref="Domain.Targets.Enums.DeploymentEnvironment"/>'s
    /// declared order (dev, prod), then slug. The environment is stored as text, which the database would sort
    /// alphabetically, so the order is applied in memory.
    /// </remarks>
    public override async Task<IReadOnlyList<TargetEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Set.AsNoTracking()
            .Join(db.Products, target => target.ProductId, product => product.Id, (target, product) => new { Target = target, Product = product.Slug })
            .ToListAsync(cancellationToken);
        return
        [
            .. rows
                .OrderBy(row => row.Product, StringComparer.Ordinal)
                .ThenBy(row => row.Target.Environment)
                .ThenBy(row => row.Target.Slug, StringComparer.Ordinal)
                .Select(row => row.Target),
        ];
    }

    /// <inheritdoc />
    public Task<TargetEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(entity => entity.Slug == slug, ct);

    /// <inheritdoc />
    public Task<bool> AnyForProductAsync(Guid productId, CancellationToken ct = default) =>
        Set.AnyAsync(entity => entity.ProductId == productId, ct);

    /// <inheritdoc />
    public Task<bool> AnyForServerAsync(Guid serverId, CancellationToken ct = default) =>
        Set.AnyAsync(entity => entity.ServerId == serverId, ct);
}
