using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Keeps cached product manifests in a dictionary behind the repository contract, counting what was written.</summary>
public sealed class InMemoryProductManifests : IProductManifestsRepository
{
    /// <summary>Gets the stored manifests, by product identifier.</summary>
    public Dictionary<Guid, ProductManifestEntity> Rows { get; } = [];

    /// <summary>Gets how many times a manifest was saved.</summary>
    public int Saves { get; private set; }

    /// <summary>Gets how many times a manifest was deleted.</summary>
    public int Deletes { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, ProductManifestEntity>> GetByProductsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ProductManifestEntity>>(
            Rows.Where(row => productIds.Contains(row.Key)).ToDictionary(row => row.Key, row => row.Value));

    /// <inheritdoc />
    public Task SaveAsync(ProductManifestEntity manifest, CancellationToken ct = default)
    {
        Saves++;
        Rows[manifest.ProductId] = manifest;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid productId, CancellationToken ct = default)
    {
        Deletes++;
        Rows.Remove(productId);
        return Task.CompletedTask;
    }
}
