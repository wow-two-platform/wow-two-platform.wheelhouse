using Wheelhouse.Domain.Pins.Enums;

namespace Wheelhouse.Api.Requests;

/// <summary>Represents the body of a request to pin a workspace, product or service.</summary>
public sealed record CreatePinApiRequest
{
    /// <summary>Gets what the pin points at: <c>workspace</c>, <c>product</c> or <c>service</c>.</summary>
    public required PinKind Kind { get; init; }

    /// <summary>Gets the workspace to pin; a workspace pin only.</summary>
    public Guid? WorkspaceId { get; init; }

    /// <summary>Gets the product to pin, or the pinned service's product, by identifier (or slug); product and service
    /// pins only.</summary>
    public string? ProductId { get; init; }

    /// <summary>Gets the service's key in the product's <c>deploy.yml</c>; a service pin only.</summary>
    public string? Service { get; init; }
}
