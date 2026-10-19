using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Vaults.Entities;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Repositories;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Keeps servers in memory.</summary>
public sealed class InMemoryServersRepository : InMemoryRepository<ServerEntity, Guid>, IServersRepository
{
    /// <inheritdoc />
    public Task<ServerEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Slug == slug));
}

/// <summary>Keeps vaults in memory.</summary>
public sealed class InMemoryVaultsRepository : InMemoryRepository<VaultEntity, Guid>, IVaultsRepository
{
    /// <inheritdoc />
    public Task<VaultEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Slug == slug));

    /// <inheritdoc />
    public Task<bool> AnyForServerAsync(Guid serverId, CancellationToken ct = default) =>
        Task.FromResult(Rows.Any(row => row.ServerId == serverId));
}

/// <summary>Keeps products in memory.</summary>
public sealed class InMemoryProductsRepository : InMemoryRepository<Wheelhouse.Domain.Products.Entities.ProductEntity, Guid>, IProductsRepository
{
    /// <inheritdoc />
    public Task<Wheelhouse.Domain.Products.Entities.ProductEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Slug == slug));

    /// <inheritdoc />
    public Task<Wheelhouse.Domain.Products.Entities.ProductEntity?> FindAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Slug == key || row.Id.ToString() == key));

    /// <inheritdoc />
    public Task<bool> AnyInWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        Task.FromResult(Rows.Any(row => row.WorkspaceId == workspaceId));
}
