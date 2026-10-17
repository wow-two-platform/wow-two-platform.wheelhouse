using Wheelhouse.Domain.Pins.Enums;

namespace Wheelhouse.Application.Pins.Models;

/// <summary>Represents a pin as the console lists it.</summary>
public sealed record PinDto
{
    /// <summary>Gets the pin's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets what the pin points at: <c>workspace</c>, <c>product</c> or <c>service</c>.</summary>
    public required PinKind Kind { get; init; }

    /// <summary>Gets the pinned workspace's identifier; set for a workspace pin only.</summary>
    public Guid? WorkspaceId { get; init; }

    /// <summary>Gets the pinned product's identifier, or the pinned service's product's; <c>null</c> for a workspace pin.</summary>
    public Guid? ProductId { get; init; }

    /// <summary>Gets the pinned service's key; set for a service pin only.</summary>
    public string? Service { get; init; }

    /// <summary>Gets the pin's place in the operator's order; lower comes first.</summary>
    public required int Order { get; init; }
}
