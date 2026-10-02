using Wheelhouse.Domain.Servers.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of the hosts Wheelhouse deploys to.</summary>
public interface IServersRepository : IRepository<ServerEntity, Guid>
{
    /// <summary>Finds the server a slug names, tracked for changes.</summary>
    /// <param name="slug">The server's slug.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The server, or <c>null</c> when none has the slug.</returns>
    Task<ServerEntity?> GetBySlugAsync(string slug, CancellationToken ct = default);
}
