using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the fleet rollouts and their products; products always come back in their fleet's order.</summary>
internal sealed class FleetRolloutsRepository(WheelhouseDbContext db) : IFleetRolloutsRepository
{
    /// <inheritdoc />
    public async Task<FleetRolloutEntity?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var fleet = await db.Set<FleetRolloutEntity>().Include(entity => entity.Items).FirstOrDefaultAsync(entity => entity.Id == id, ct);
        fleet?.Items.Sort((left, right) => left.Position.CompareTo(right.Position));
        return fleet;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetRunningIdsAsync(CancellationToken ct = default) =>
        await db.Set<FleetRolloutEntity>().AsNoTracking()
            .Where(entity => entity.Status == FleetRolloutStatus.Running)
            .Select(entity => entity.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FleetRolloutEntity>> ListAsync(int limit, CancellationToken ct = default)
    {
        var fleets = await db.Set<FleetRolloutEntity>().AsNoTracking()
            .OrderByDescending(entity => entity.CreatedAt)
            .Take(limit)
            .Include(entity => entity.Items)
            .ToListAsync(ct);
        foreach (var fleet in fleets)
            fleet.Items.Sort((left, right) => left.Position.CompareTo(right.Position));
        return fleets;
    }

    /// <inheritdoc />
    public async Task<bool> AddAsync(FleetRolloutEntity fleet, CancellationToken ct = default)
    {
        db.Set<FleetRolloutEntity>().Add(fleet);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException) when (fleet.Status == FleetRolloutStatus.Running)
        {
            // One fleet runs at a time (ix_fleet_rollouts_one_running): a concurrent start won the race.
            db.Entry(fleet).State = EntityState.Detached;
            foreach (var item in fleet.Items)
                db.Entry(item).State = EntityState.Detached;
            if ((await GetRunningIdsAsync(ct)).Any(id => id != fleet.Id))
                return false;
            throw;
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
