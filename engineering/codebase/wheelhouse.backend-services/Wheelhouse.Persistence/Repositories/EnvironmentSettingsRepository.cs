using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses products' non-secret setting values per environment.</summary>
internal sealed class EnvironmentSettingsRepository(WheelhouseDbContext db) : IEnvironmentSettingsRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EnvironmentSettingEntity>> GetAsync(
        Guid productId, DeploymentEnvironment environment, CancellationToken ct = default) =>
        await db.EnvironmentSettings.AsNoTracking()
            .Where(entity => entity.ProductId == productId && entity.Environment == environment)
            .OrderBy(entity => entity.Key)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task SetAsync(Guid productId, DeploymentEnvironment environment, IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        var keys = values.Keys.ToList();
        var rows = await db.EnvironmentSettings
            .Where(entity => entity.ProductId == productId && entity.Environment == environment && keys.Contains(entity.Key))
            .ToListAsync(ct);
        foreach (var (key, value) in values)
        {
            var row = rows.FirstOrDefault(entity => entity.Key == key);
            if (row is null)
                db.EnvironmentSettings.Add(new EnvironmentSettingEntity
                    { ProductId = productId, Environment = environment, Key = key, Value = value });
            else
                row.Value = value;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> RemoveAsync(
        Guid productId, DeploymentEnvironment environment, IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        if (keys.Count == 0)
            return [];
        var wanted = keys.ToList();
        var rows = await db.EnvironmentSettings
            .Where(entity => entity.ProductId == productId && entity.Environment == environment && wanted.Contains(entity.Key))
            .ToListAsync(ct);
        db.EnvironmentSettings.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        return [.. rows.Select(row => row.Key).Order(StringComparer.Ordinal)];
    }
}
