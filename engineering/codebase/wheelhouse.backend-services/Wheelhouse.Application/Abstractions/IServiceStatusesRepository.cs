using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of the statuses the operator gave products' services; a service without one is active.</summary>
public interface IServiceStatusesRepository
{
    /// <summary>Gets every recorded service status, by product then service.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The recorded statuses.</returns>
    Task<IReadOnlyList<ServiceStatusEntity>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Records a service's status, adding its row or changing it.</summary>
    /// <param name="productId">The product the service belongs to.</param>
    /// <param name="service">The service's key.</param>
    /// <param name="status">The service's new status.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SetAsync(Guid productId, string service, ProductStatus status, CancellationToken ct = default);
}
