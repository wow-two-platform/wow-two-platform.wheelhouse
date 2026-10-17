namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to rename or redescribe a workspace.</summary>
public sealed record UpdateWorkspaceApiRequest
{
    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets one line on what the workspace holds, or <c>null</c> for none.</summary>
    public string? Description { get; init; }
}
