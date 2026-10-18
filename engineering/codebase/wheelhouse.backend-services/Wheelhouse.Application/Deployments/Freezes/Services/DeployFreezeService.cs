using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Freezes.Models;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Application.Deployments.Freezes.Services;

/// <summary>Reads the holds on deploys and decides which one covers a target: its product's environment, its product,
/// or everything, the narrowest first.</summary>
public sealed class DeployFreezeService(IDeployFreezesRepository freezes, IProductsRepository products)
{
    /// <summary>Holds the scope names the wire carries.</summary>
    public const string Portfolio = "portfolio";

    /// <summary>Lists every freeze with its product's slug, oldest first; a freeze of a product the inventory no longer
    /// holds is gone with it.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The freezes.</returns>
    public async Task<IReadOnlyList<DeployFreezeDto>> ListAsync(CancellationToken ct)
    {
        var slugs = (await products.GetAllAsync(ct)).ToDictionary(row => row.Id, row => row.Slug);
        return [.. (await freezes.GetAllAsync(ct))
            .Where(row => row.ProductId is not { } id || slugs.ContainsKey(id))
            .Select(row => Map(row, row.ProductId is { } id ? slugs[id] : null))];
    }

    /// <summary>Finds the freeze covering a product's environment, the narrowest first.</summary>
    /// <param name="productId">The target's product.</param>
    /// <param name="environment">The target's environment.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The freeze, or <c>null</c> when nothing holds deploys there.</returns>
    public async Task<DeployFreezeEntity?> CoveringAsync(Guid productId, DeploymentEnvironment environment, CancellationToken ct) =>
        Covering(await freezes.GetAllAsync(ct), productId, environment);

    /// <summary>The freeze among <paramref name="all"/> covering a product's environment, the narrowest first.</summary>
    /// <param name="all">Every freeze.</param>
    /// <param name="productId">The target's product.</param>
    /// <param name="environment">The target's environment.</param>
    /// <returns>The freeze, or <c>null</c>.</returns>
    public static DeployFreezeEntity? Covering(IEnumerable<DeployFreezeEntity> all, Guid productId, DeploymentEnvironment environment)
    {
        var list = all.ToList();
        return list.FirstOrDefault(row => row.ProductId == productId && row.Environment == environment)
            ?? list.FirstOrDefault(row => row.ProductId == productId && row.Environment is null)
            ?? list.FirstOrDefault(row => row.ProductId is null);
    }

    /// <summary>Maps a freeze.</summary>
    /// <param name="row">The stored freeze.</param>
    /// <param name="product">Its product's slug, or null for the portfolio.</param>
    /// <returns>The freeze as the API answers it.</returns>
    public static DeployFreezeDto Map(DeployFreezeEntity row, string? product) => new(
        row.Id,
        row.ProductId is null ? Portfolio : row.Environment is null ? "product" : "environment",
        product,
        row.Environment?.ToString().ToLowerInvariant(),
        row.Reason,
        row.Actor,
        row.CreatedAt);
}
