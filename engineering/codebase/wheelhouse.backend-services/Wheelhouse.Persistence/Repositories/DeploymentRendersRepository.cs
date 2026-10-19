using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Deployments.Entities;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses what each deploy rendered, by fingerprint and without values.</summary>
internal sealed class DeploymentRendersRepository(WheelhouseDbContext db) : IDeploymentRenders
{
    /// <inheritdoc />
    public async Task AddAsync(DeploymentRenderEntity render, CancellationToken ct = default)
    {
        db.DeploymentRenders.Add(render);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<DeploymentRenderEntity?> GetAsync(Guid jobId, CancellationToken ct = default) =>
        await db.DeploymentRenders.AsNoTracking().FirstOrDefaultAsync(render => render.JobId == jobId, ct);

    /// <inheritdoc />
    public async Task<DeploymentRenderEntity?> GetLatestAsync(string target, CancellationToken ct = default) =>
        (await db.DeploymentRenders.AsNoTracking().Where(render => render.Target == target).ToListAsync(ct))
        .OrderByDescending(render => render.At).FirstOrDefault();

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetChangesAsync(CancellationToken ct = default) =>
        (await db.DeploymentRenders.AsNoTracking()
            .Where(render => render.Changes != null && render.Changes != "[]")
            .Select(render => new { render.JobId, render.Changes })
            .ToListAsync(ct))
        .ToDictionary(render => render.JobId.ToString(), render => render.Changes!, StringComparer.Ordinal);
}
