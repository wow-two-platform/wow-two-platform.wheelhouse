namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to add a workspace.</summary>
public sealed record CreateWorkspaceApiRequest
{
    /// <summary>Gets the name the catalog will know the workspace by.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the workspace holds, or <c>null</c> for none.</summary>
    public string? Description { get; init; }
}
