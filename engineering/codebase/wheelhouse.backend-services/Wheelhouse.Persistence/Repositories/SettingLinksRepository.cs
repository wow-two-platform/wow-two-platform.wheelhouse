using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses owners' links from a product environment's key to another product's key.</summary>
internal sealed class SettingLinksRepository(WheelhouseDbContext db) : ISettingLinksRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingLinkEntity>> GetAsync(
        Guid productId, DeploymentEnvironment environment, CancellationToken ct = default) =>
        await db.SettingLinks.AsNoTracking()
            .Where(link => link.ProductId == productId && link.Environment == environment)
            .OrderBy(link => link.Key)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingLinkEntity>> GetBySourceAsync(
        Guid productId, DeploymentEnvironment environment, string key, CancellationToken ct = default) =>
        await db.SettingLinks.AsNoTracking()
            .Where(link => link.SourceKey == key)
            .Join(db.Targets.Where(target => target.ProductId == productId && target.Environment == environment),
                link => link.SourceTargetId, target => target.Id, (link, _) => link)
            .OrderBy(link => link.Key)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task SetAsync(SettingLinkEntity link, CancellationToken ct = default)
    {
        var row = await db.SettingLinks.FirstOrDefaultAsync(item =>
            item.ProductId == link.ProductId && item.Environment == link.Environment && item.Key == link.Key, ct);
        if (row is not null)
        {
            db.SettingLinks.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        db.SettingLinks.Add(link);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid productId, DeploymentEnvironment environment, string key, CancellationToken ct = default)
    {
        var row = await db.SettingLinks.FirstOrDefaultAsync(item =>
            item.ProductId == productId && item.Environment == environment && item.Key == key, ct);
        if (row is null)
            return false;
        db.SettingLinks.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
