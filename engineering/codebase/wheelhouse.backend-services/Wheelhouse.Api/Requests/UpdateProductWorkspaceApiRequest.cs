namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to move a product into another workspace.</summary>
public sealed record UpdateProductWorkspaceApiRequest
{
    /// <summary>Gets the workspace the product moves into.</summary>
    public required Guid WorkspaceId { get; init; }
}
