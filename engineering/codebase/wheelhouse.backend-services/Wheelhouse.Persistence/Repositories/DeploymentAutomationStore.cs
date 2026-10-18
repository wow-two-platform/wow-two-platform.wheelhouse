using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the products' automation choices, the release watermarks and the automatic deploy queue.</summary>
internal sealed class DeploymentAutomationStore(WheelhouseDbContext db) : IDeploymentAutomationStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductAutomationEntity>> GetSettingsAsync(CancellationToken ct = default) =>
        await db.Set<ProductAutomationEntity>().AsNoTracking().ToListAsync(ct);

    /// <inheritdoc />
    public async Task<ProductAutomationEntity> SetSettingsAsync(
        Guid productId, bool? autoDeployDev, bool? autoPromote, CancellationToken ct = default)
    {
        var row = await db.Set<ProductAutomationEntity>().FirstOrDefaultAsync(entity => entity.ProductId == productId, ct);
        if (row is null)
        {
            row = new ProductAutomationEntity { ProductId = productId };
            db.Set<ProductAutomationEntity>().Add(row);
        }

        row.AutoDeployDev = autoDeployDev ?? row.AutoDeployDev;
        row.AutoPromote = autoPromote ?? row.AutoPromote;
        await db.SaveChangesAsync(ct);
        return row;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReleaseWatermarkEntity>> GetWatermarksAsync(CancellationToken ct = default) =>
        await db.Set<ReleaseWatermarkEntity>().AsNoTracking().ToListAsync(ct);

    /// <inheritdoc />
    public async Task SetWatermarkAsync(ReleaseWatermarkEntity watermark, CancellationToken ct = default)
    {
        var row = await db.Set<ReleaseWatermarkEntity>().FirstOrDefaultAsync(entity => entity.ProductId == watermark.ProductId, ct);
        if (row is null)
            db.Set<ReleaseWatermarkEntity>().Add(watermark);
        else
        {
            row.Release = watermark.Release;
            row.PublishedAt = watermark.PublishedAt;
            row.CheckedAt = watermark.CheckedAt;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedDeploymentEntity>> GetQueueAsync(CancellationToken ct = default) =>
        (await db.Set<QueuedDeploymentEntity>().AsNoTracking().ToListAsync(ct))
        .OrderBy(entity => entity.QueuedAt)
        .ToList();

    /// <inheritdoc />
    public async Task SetQueuedAsync(QueuedDeploymentEntity item, CancellationToken ct = default)
    {
        var row = await db.Set<QueuedDeploymentEntity>().FirstOrDefaultAsync(entity => entity.TargetId == item.TargetId, ct);
        if (row is null)
            db.Set<QueuedDeploymentEntity>().Add(item);
        else
        {
            row.BundleId = item.BundleId;
            row.Release = item.Release;
            row.PublishedAt = item.PublishedAt;
            row.Cause = item.Cause;
            row.QueuedAt = item.QueuedAt;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AutoPromotionEntity>> GetPromotionsAsync(CancellationToken ct = default) =>
        await db.Set<AutoPromotionEntity>().AsNoTracking().ToListAsync(ct);

    /// <inheritdoc />
    public async Task SetPromotionAsync(AutoPromotionEntity mark, CancellationToken ct = default)
    {
        var row = await db.Set<AutoPromotionEntity>().FirstOrDefaultAsync(entity => entity.TargetId == mark.TargetId, ct);
        if (row is null)
            db.Set<AutoPromotionEntity>().Add(mark);
        else
        {
            row.Release = mark.Release;
            row.AttemptedAt = mark.AttemptedAt;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task RemoveQueuedAsync(Guid targetId, CancellationToken ct = default) =>
        await db.Set<QueuedDeploymentEntity>().Where(entity => entity.TargetId == targetId).ExecuteDeleteAsync(ct);
}
