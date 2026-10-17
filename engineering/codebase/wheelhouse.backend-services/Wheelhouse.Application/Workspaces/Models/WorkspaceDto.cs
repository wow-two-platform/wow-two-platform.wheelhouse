namespace Wheelhouse.Application.Workspaces.Models;

/// <summary>Represents a workspace as the console lists it.</summary>
public sealed record WorkspaceDto
{
    /// <summary>Gets the workspace's identifier; products and pins refer to it.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the name the catalog knows the workspace by.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the workspace holds, or <c>null</c> for none.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the workspace's place in the operator's order; lower comes first.</summary>
    public required int Order { get; init; }
}
