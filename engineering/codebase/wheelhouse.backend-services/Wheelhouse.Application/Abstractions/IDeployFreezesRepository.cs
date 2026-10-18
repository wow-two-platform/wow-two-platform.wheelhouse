using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Targets.Enums;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of holds on deploys.</summary>
public interface IDeployFreezesRepository
{
    /// <summary>Gets every freeze, oldest first.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The freezes.</returns>
    Task<IReadOnlyList<DeployFreezeEntity>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Finds the freeze of one scope.</summary>
    /// <param name="productId">The product, or null for everything.</param>
    /// <param name="environment">The product's environment, or null for all of them.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The freeze, or <c>null</c> when that scope is not frozen.</returns>
    Task<DeployFreezeEntity?> FindAsync(Guid? productId, DeploymentEnvironment? environment, CancellationToken ct = default);

    /// <summary>Stores a new freeze.</summary>
    /// <param name="freeze">The freeze.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The stored freeze.</returns>
    Task<DeployFreezeEntity> AddAsync(DeployFreezeEntity freeze, CancellationToken ct = default);

    /// <summary>Lifts a freeze.</summary>
    /// <param name="id">The freeze's identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
