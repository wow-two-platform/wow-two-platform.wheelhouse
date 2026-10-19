using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Mappers;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Products.Services;

/// <summary>Provides products as integrations read them, joined with their identity, their environments, the vault
/// namespace each environment's settings belong in, the sites its newest rollout published and the services marked
/// inactive.</summary>
public sealed class ProductsProjectionService(
    ITargetsRepository targets,
    IVaultsRepository vaults,
    ITargetSites sites,
    IServiceStatusesRepository serviceStatuses,
    ProductIdentityService identities)
{
    /// <summary>Projects products for integrations, refreshing the identity of any whose branch head moved.</summary>
    /// <param name="products">The stored products, in the order to return them.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The products integrations read.</returns>
    public async Task<IReadOnlyList<ProductDto>> ProjectAsync(IReadOnlyList<ProductEntity> products, CancellationToken ct) =>
        await ProjectAsync(products, await identities.ResolveAsync(products, ct), ct);

    /// <summary>Projects products for integrations with identities already resolved.</summary>
    /// <param name="products">The stored products, in the order to return them.</param>
    /// <param name="resolved">Each product's identity, keyed by product identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The products integrations read.</returns>
    public async Task<IReadOnlyList<ProductDto>> ProjectAsync(
        IReadOnlyList<ProductEntity> products, IReadOnlyDictionary<Guid, ProductIdentity> resolved, CancellationToken ct)
    {
        var allTargets = await targets.GetAllAsync(ct);
        // A server's first vault, by slug, holds its products' settings; a server without one uses the inventory's first
        // (the control host's), as a deploy's settings render does.
        var allVaults = await vaults.GetAllAsync(ct);
        var vaultByServer = allVaults
            .GroupBy(vault => vault.ServerId)
            .ToDictionary(group => group.Key, group => group.Select(vault => vault.Slug).Min(StringComparer.Ordinal)!);
        if (allVaults.Select(vault => vault.Slug).Min(StringComparer.Ordinal) is { } first)
            foreach (var server in allTargets.Select(target => target.ServerId).Distinct())
                vaultByServer.TryAdd(server, first);
        var published = await sites.ReadAsync(ct);
        var inactive = (await serviceStatuses.GetAllAsync(ct))
            .Where(row => row.Status == ProductStatus.Inactive)
            .ToLookup(row => row.ProductId, row => row.Service);
        return
        [
            .. products.Select(product => ProductMapper.Map(
                product, resolved[product.Id], allTargets.Where(target => target.ProductId == product.Id), vaultByServer,
                published, inactive[product.Id])),
        ];
    }

    /// <summary>Projects one product for integrations.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The product integrations read.</returns>
    public async Task<ProductDto> ProjectAsync(ProductEntity product, CancellationToken ct) =>
        (await ProjectAsync([product], ct))[0];

    /// <summary>Projects one product for integrations with its identity already resolved.</summary>
    /// <param name="product">The stored product.</param>
    /// <param name="identity">What the product is called and described as.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The product integrations read.</returns>
    public async Task<ProductDto> ProjectAsync(ProductEntity product, ProductIdentity identity, CancellationToken ct) =>
        (await ProjectAsync([product], new Dictionary<Guid, ProductIdentity> { [product.Id] = identity }, ct))[0];
}
