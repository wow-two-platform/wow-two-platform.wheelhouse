using Wheelhouse.Domain.Settings.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of owners' links from a product environment's key to another product's key.</summary>
public interface ISettingLinksRepository
{
    /// <summary>Gets a product environment's links in key order.</summary>
    /// <param name="productId">The linking product's identifier.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The links.</returns>
    Task<IReadOnlyList<SettingLinkEntity>> GetAsync(Guid productId, DeploymentEnvironment environment, CancellationToken ct = default);

    /// <summary>Gets the links that take a product environment's key as their source, whichever of its targets they
    /// name.</summary>
    /// <param name="productId">The source's product.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="key">The source key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The links, by linking key.</returns>
    Task<IReadOnlyList<SettingLinkEntity>> GetBySourceAsync(
        Guid productId, DeploymentEnvironment environment, string key, CancellationToken ct = default);

    /// <summary>Adds a link, or replaces the one its product, environment and key had.</summary>
    /// <param name="link">The link.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SetAsync(SettingLinkEntity link, CancellationToken ct = default);

    /// <summary>Removes a link; false when the key had none.</summary>
    /// <param name="productId">The linking product's identifier.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="key">The linking key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Whether a link was removed.</returns>
    Task<bool> RemoveAsync(Guid productId, DeploymentEnvironment environment, string key, CancellationToken ct = default);
}
