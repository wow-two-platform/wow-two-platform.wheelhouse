using Wheelhouse.Domain.Pins.Entities;
using Wheelhouse.Domain.Pins.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of the owners' pins; every read is one owner's.</summary>
public interface IPinsRepository : IRepository<PinEntity, Guid>
{
    /// <summary>Gets one owner's pins, in the owner's order.</summary>
    /// <param name="owner">The owner's login.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The owner's pins.</returns>
    Task<IReadOnlyList<PinEntity>> GetForOwnerAsync(string owner, CancellationToken ct = default);

    /// <summary>Finds an owner's pin on a target.</summary>
    /// <param name="owner">The owner's login.</param>
    /// <param name="kind">What the pin points at.</param>
    /// <param name="workspaceId">The pinned workspace, for a workspace pin.</param>
    /// <param name="productId">The pinned product, or the pinned service's product.</param>
    /// <param name="service">The pinned service's key, for a service pin.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The pin, or <c>null</c> when the owner has not pinned the target.</returns>
    Task<PinEntity?> FindAsync(
        string owner, PinKind kind, Guid? workspaceId, Guid? productId, string? service, CancellationToken ct = default);

    /// <summary>Gets the place an owner's next pin takes: one after the owner's last.</summary>
    /// <param name="owner">The owner's login.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The next order value; <c>0</c> for an owner with no pin.</returns>
    Task<int> NextOrderAsync(string owner, CancellationToken ct = default);

    /// <summary>Gives each of an owner's pins the place its identifier holds in <paramref name="ids"/>, in one save.</summary>
    /// <param name="owner">The owner's login.</param>
    /// <param name="ids">Every one of the owner's pin identifiers, in the new order.</param>
    /// <param name="ct">A cancellation token.</param>
    Task ReorderAsync(string owner, IReadOnlyList<Guid> ids, CancellationToken ct = default);
}
