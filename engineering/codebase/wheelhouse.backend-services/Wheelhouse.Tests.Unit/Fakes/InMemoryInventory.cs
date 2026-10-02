using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Servers.Entities;
using Wheelhouse.Domain.Vaults.Entities;

namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Keeps servers in memory.</summary>
public sealed class InMemoryServersRepository : InMemoryRepository<ServerEntity>, IServersRepository
{
    /// <inheritdoc />
    public Task<ServerEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Slug == slug));
}

/// <summary>Keeps vaults in memory.</summary>
public sealed class InMemoryVaultsRepository : InMemoryRepository<VaultEntity>, IVaultsRepository
{
    /// <inheritdoc />
    public Task<VaultEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Slug == slug));

    /// <inheritdoc />
    public Task<bool> AnyForServerAsync(Guid serverId, CancellationToken ct = default) =>
        Task.FromResult(Rows.Any(row => row.ServerId == serverId));
}
