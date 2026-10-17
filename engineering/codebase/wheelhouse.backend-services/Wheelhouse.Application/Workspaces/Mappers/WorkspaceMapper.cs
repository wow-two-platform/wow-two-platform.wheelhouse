using Wheelhouse.Application.Workspaces.Models;
using Wheelhouse.Domain.Workspaces.Entities;

namespace Wheelhouse.Application.Workspaces.Mappers;

/// <summary>Maps a stored workspace to the one the console lists.</summary>
internal static class WorkspaceMapper
{
    /// <summary>Maps a stored workspace to its projection.</summary>
    /// <param name="workspace">The stored workspace.</param>
    /// <returns>The workspace the console lists.</returns>
    public static WorkspaceDto Map(WorkspaceEntity workspace) => new()
    {
        Id = workspace.Id,
        Slug = workspace.Slug,
        Name = workspace.Name,
        Description = workspace.Description,
        Order = workspace.Order,
    };
}
