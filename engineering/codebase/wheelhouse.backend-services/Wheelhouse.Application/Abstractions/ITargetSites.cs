using Wheelhouse.Application.Products.Models;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the read of the sites each target's newest succeeded rollout published.</summary>
public interface ITargetSites
{
    /// <summary>Reads every target's published sites, keyed by target slug.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The sites per target; empty when the runner cannot be read, so a product read never fails on it.</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<ProductSiteModel>>> ReadAsync(CancellationToken ct);
}
