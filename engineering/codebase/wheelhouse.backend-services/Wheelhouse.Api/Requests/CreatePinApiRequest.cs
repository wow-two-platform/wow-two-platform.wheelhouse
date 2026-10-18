using Wheelhouse.Domain.Pins.Enums;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to pin a product; workspace and service pins are refused.</summary>
public sealed record CreatePinApiRequest
{
    /// <summary>Gets what the pin points at: <c>product</c>; <c>workspace</c> and <c>service</c> answer 400.</summary>
    public required PinKind Kind { get; init; }

    /// <summary>Gets a workspace; refused.</summary>
    public Guid? WorkspaceId { get; init; }

    /// <summary>Gets the product to pin, by identifier (or slug).</summary>
    public string? ProductId { get; init; }

    /// <summary>Gets a service key; refused.</summary>
    public string? Service { get; init; }
}
