using Microsoft.EntityFrameworkCore;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Accesses the statuses the operator gave products' services.</summary>
internal sealed class ServiceStatusesRepository(WheelhouseDbContext db) : IServiceStatusesRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceStatusEntity>> GetAllAsync(CancellationToken ct = default) =>
        await db.ServiceStatuses.AsNoTracking()
            .OrderBy(entity => entity.ProductId)
            .ThenBy(entity => entity.Service)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task SetAsync(Guid productId, string service, ProductStatus status, CancellationToken ct = default)
    {
        var row = await db.ServiceStatuses.FirstOrDefaultAsync(entity => entity.ProductId == productId && entity.Service == service, ct);
        if (row is null)
            db.ServiceStatuses.Add(new ServiceStatusEntity { ProductId = productId, Service = service, Status = status });
        else
            row.Status = status;
        await db.SaveChangesAsync(ct);
    }
}
