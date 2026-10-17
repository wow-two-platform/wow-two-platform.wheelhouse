using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Pins.Entities;
using Wheelhouse.Domain.Pins.Enums;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Repositories;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the operators' pins, one owner at a time.</summary>
internal sealed class PinsRepository(WheelhouseDbContext db) : EfRepository<PinEntity, Guid>(db), IPinsRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PinEntity>> GetForOwnerAsync(string owner, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(pin => pin.Owner == owner)
            .OrderBy(pin => pin.Order)
            .ThenBy(pin => pin.CreatedAt)
            .ThenBy(pin => pin.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<PinEntity?> FindAsync(
        string owner, PinKind kind, Guid? workspaceId, Guid? productId, string? service, CancellationToken ct = default) =>
        Set.AsNoTracking().FirstOrDefaultAsync(
            pin => pin.Owner == owner && pin.Kind == kind && pin.WorkspaceId == workspaceId && pin.ProductId == productId
                && pin.Service == service,
            ct);

    /// <inheritdoc />
    public async Task<int> NextOrderAsync(string owner, CancellationToken ct = default) =>
        (await Set.Where(pin => pin.Owner == owner).MaxAsync(pin => (int?)pin.Order, ct) ?? -1) + 1;

    /// <inheritdoc />
    public async Task ReorderAsync(string owner, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var tracked = await Set.Where(pin => pin.Owner == owner && ids.Contains(pin.Id)).ToDictionaryAsync(pin => pin.Id, ct);
        for (var place = 0; place < ids.Count; place++)
            if (tracked.TryGetValue(ids[place], out var pin))
                pin.Order = place;
        await Context.SaveChangesAsync(ct);
    }
}
