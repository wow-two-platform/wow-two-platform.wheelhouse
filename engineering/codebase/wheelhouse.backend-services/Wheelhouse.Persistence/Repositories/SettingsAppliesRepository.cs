using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the scheduled applies of edited settings, one per target.</summary>
internal sealed class SettingsAppliesRepository(WheelhouseDbContext db) : ISettingsApplies
{
    /// <inheritdoc />
    public async Task<SettingsApplyEntity?> GetAsync(Guid targetId, CancellationToken ct = default) =>
        await db.SettingsApplies.AsNoTracking().FirstOrDefaultAsync(apply => apply.TargetId == targetId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingsApplyEntity>> GetDueAsync(DateTimeOffset now, CancellationToken ct = default) =>
        [.. (await db.SettingsApplies.AsNoTracking().ToListAsync(ct)).Where(apply => apply.At <= now).OrderBy(apply => apply.At)];

    /// <inheritdoc />
    public async Task SetAsync(SettingsApplyEntity apply, CancellationToken ct = default)
    {
        var row = await db.SettingsApplies.FirstOrDefaultAsync(item => item.TargetId == apply.TargetId, ct);
        if (row is not null)
            db.SettingsApplies.Remove(row);
        if (row is not null)
            await db.SaveChangesAsync(ct);
        db.SettingsApplies.Add(apply);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid targetId, CancellationToken ct = default)
    {
        var row = await db.SettingsApplies.FirstOrDefaultAsync(item => item.TargetId == targetId, ct);
        if (row is null)
            return false;
        db.SettingsApplies.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
