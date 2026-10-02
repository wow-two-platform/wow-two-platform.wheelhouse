using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Mappers;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Entities;

namespace Wheelhouse.Application.Products.Services;

/// <summary>Provides products as integrations read them, joined with their environments, the vault namespace each
/// environment's settings belong in, and the sites its newest rollout published.</summary>
public sealed class ProductsProjectionService(ITargetsRepository targets, IVaultsRepository vaults, ITargetSites sites)
{
    /// <summary>Projects products for integrations.</summary>
    /// <param name="products">The stored products, in the order to return them.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The products integrations read.</returns>
    public async Task<IReadOnlyList<ProductDto>> ProjectAsync(IReadOnlyList<ProductEntity> products, CancellationToken ct)
    {
        var allTargets = await targets.GetAllAsync(ct);
        // A server's first vault, by slug, holds its products' settings.
        var vaultByServer = (await vaults.GetAllAsync(ct))
            .GroupBy(vault => vault.ServerId)
            .ToDictionary(group => group.Key, group => group.Select(vault => vault.Slug).Min(StringComparer.Ordinal)!);
        var published = await sites.ReadAsync(ct);
        return
        [
            .. products.Select(product => ProductMapper.Map(
                product, allTargets.Where(target => target.ProductId == product.Id), vaultByServer, published)),
        ];
    }

    /// <summary>Projects one product for integrations.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The product integrations read.</returns>
    public async Task<ProductDto> ProjectAsync(ProductEntity product, CancellationToken ct) =>
        (await ProjectAsync([product], ct))[0];
}
