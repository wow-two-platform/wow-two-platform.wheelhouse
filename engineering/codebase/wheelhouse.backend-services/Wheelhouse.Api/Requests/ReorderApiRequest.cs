namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to put a list in a new order.</summary>
public sealed record ReorderApiRequest
{
    /// <summary>Gets every item's identifier, first to last.</summary>
    public required IReadOnlyList<Guid> Ids { get; init; }
}
