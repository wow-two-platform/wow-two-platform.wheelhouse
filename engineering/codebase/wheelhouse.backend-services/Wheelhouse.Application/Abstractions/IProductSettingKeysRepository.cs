using Wheelhouse.Domain.Settings.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of keys owners added to products in the console.</summary>
public interface IProductSettingKeysRepository
{
    /// <summary>Gets a product's added keys in key order.</summary>
    Task<IReadOnlyList<ProductSettingKeyEntity>> GetAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Adds a key, or replaces the one with its name.</summary>
    Task SetAsync(ProductSettingKeyEntity key, CancellationToken ct = default);

    /// <summary>Removes a key; false when the product has no such key.</summary>
    Task<bool> RemoveAsync(Guid productId, string key, CancellationToken ct = default);
}
