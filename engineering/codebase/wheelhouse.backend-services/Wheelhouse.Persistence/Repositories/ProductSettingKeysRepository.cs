using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Settings.Entities;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the setting and secret keys owners added to products in the console.</summary>
internal sealed class ProductSettingKeysRepository(WheelhouseDbContext db) : IProductSettingKeysRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductSettingKeyEntity>> GetAsync(Guid productId, CancellationToken ct = default) =>
        await db.ProductSettingKeys.AsNoTracking().Where(key => key.ProductId == productId).OrderBy(key => key.Key).ToListAsync(ct);

    /// <inheritdoc />
    public async Task SetAsync(ProductSettingKeyEntity key, CancellationToken ct = default)
    {
        var row = await db.ProductSettingKeys.FirstOrDefaultAsync(item => item.ProductId == key.ProductId && item.Key == key.Key, ct);
        if (row is null)
            db.ProductSettingKeys.Add(key);
        else
        {
            row.Secret = key.Secret;
            row.Type = key.Type;
            row.Format = key.Format;
            row.Choices = key.Choices;
            row.Required = key.Required;
            row.Note = key.Note;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid productId, string key, CancellationToken ct = default)
    {
        var row = await db.ProductSettingKeys.FirstOrDefaultAsync(item => item.ProductId == productId && item.Key == key, ct);
        if (row is null)
            return false;
        db.ProductSettingKeys.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
