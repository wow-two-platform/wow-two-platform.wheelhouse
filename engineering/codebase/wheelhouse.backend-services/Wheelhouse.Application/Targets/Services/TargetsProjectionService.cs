using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Targets.Mappers;
using Wheelhouse.Application.Targets.Models;
using Wheelhouse.Domain.Targets.Entities;

namespace Wheelhouse.Application.Targets.Services;

/// <summary>Provides targets as the operator edits them, naming each one's product and server by slug.</summary>
public sealed class TargetsProjectionService(IProductsRepository products, IServersRepository servers)
{
    /// <summary>Projects targets for the operator.</summary>
    /// <param name="targets">The stored targets, in the order to return them.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The targets the operator edits.</returns>
    public async Task<IReadOnlyList<TargetDto>> ProjectAsync(IReadOnlyList<TargetEntity> targets, CancellationToken ct)
    {
        var productSlugs = (await products.GetAllAsync(ct)).ToDictionary(product => product.Id, product => product.Slug);
        var serverSlugs = (await servers.GetAllAsync(ct)).ToDictionary(server => server.Id, server => server.Slug);
        return [.. targets.Select(target => TargetMapper.Map(target, productSlugs[target.ProductId], serverSlugs[target.ServerId]))];
    }
}
