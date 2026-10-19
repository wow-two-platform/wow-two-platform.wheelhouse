using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of fleet rollouts and their products.</summary>
public interface IFleetRolloutsRepository
{
    /// <summary>Gets one fleet with its products, tracked so <see cref="SaveAsync"/> stores changes to it.</summary>
    /// <param name="id">The fleet.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The fleet, or <c>null</c> when there is none.</returns>
    Task<FleetRolloutEntity?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gets the identifiers of the fleets that run; one at most.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The identifiers.</returns>
    Task<IReadOnlyList<Guid>> GetRunningIdsAsync(CancellationToken ct = default);

    /// <summary>Lists the newest fleets with their products, newest first.</summary>
    /// <param name="limit">How many at most.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The fleets.</returns>
    Task<IReadOnlyList<FleetRolloutEntity>> ListAsync(int limit, CancellationToken ct = default);

    /// <summary>Adds a fleet with its products, tracked like one read through <see cref="GetAsync"/>.</summary>
    /// <param name="fleet">The fleet.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><c>false</c> when another running fleet won the race to be added; nothing was stored.</returns>
    Task<bool> AddAsync(FleetRolloutEntity fleet, CancellationToken ct = default);

    /// <summary>Stores the changes to the fleets read through <see cref="GetAsync"/>.</summary>
    /// <param name="ct">A cancellation token.</param>
    Task SaveAsync(CancellationToken ct = default);
}
