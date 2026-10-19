using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of products' non-secret setting values per environment; secret values live in the
/// vault.</summary>
public interface IEnvironmentSettingsRepository
{
    /// <summary>Gets a product environment's stored values, by key.</summary>
    /// <param name="productId">The product's identifier.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The stored values in key order.</returns>
    Task<IReadOnlyList<EnvironmentSettingEntity>> GetAsync(
        Guid productId, DeploymentEnvironment environment, CancellationToken ct = default);

    /// <summary>Adds or replaces values of a product environment, keeping every other key.</summary>
    /// <param name="productId">The product's identifier.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="values">The values, by key.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SetAsync(Guid productId, DeploymentEnvironment environment, IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default);

    /// <summary>Removes values of a product environment; keys it lacks are passed over.</summary>
    /// <param name="productId">The product's identifier.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="keys">The keys.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The keys it removed.</returns>
    Task<IReadOnlyList<string>> RemoveAsync(Guid productId, DeploymentEnvironment environment, IReadOnlyCollection<string> keys,
        CancellationToken ct = default);
}
