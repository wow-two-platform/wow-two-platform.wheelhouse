using Wheelhouse.Application.Integrations.Models;
using Wheelhouse.Domain.Integrations.Entities;

namespace Wheelhouse.Application.Integrations.Mappers;

/// <summary>Maps a stored integration key to the key the owner sees.</summary>
internal static class IntegrationKeyMapper
{
    /// <summary>Maps a stored key to its projection, leaving the hash behind.</summary>
    /// <param name="key">The stored key.</param>
    /// <returns>The key the owner sees.</returns>
    public static IntegrationKeyDto Map(IntegrationKeyEntity key) => new()
    {
        Id = key.Id,
        Name = key.Name,
        Prefix = key.Prefix,
        Scopes = key.ScopeList,
        CreatedBy = key.CreatedBy,
        CreatedAt = key.CreatedAt,
        LastUsedAt = key.LastUsedAt,
        RevokedAt = key.RevokedAt,
    };
}
