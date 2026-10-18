using Wheelhouse.Application.Pins.Models;
using Wheelhouse.Domain.Pins.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Pins.Commands;

/// <summary>Represents a command to pin a workspace, product or service for the signed-in owner; pinning a target
/// again answers the existing pin. A preference, so it is not audited.</summary>
public sealed record PinCreateCommand : ICommand<AppResult<PinCreated>>
{
    /// <summary>Gets what the pin points at.</summary>
    public required PinKind Kind { get; init; }

    /// <summary>Gets the workspace to pin; required for a workspace pin, refused otherwise.</summary>
    public Guid? WorkspaceId { get; init; }

    /// <summary>Gets the product to pin, or the pinned service's product, by identifier or slug; required for a product
    /// or service pin, refused for a workspace pin.</summary>
    public string? Product { get; init; }

    /// <summary>Gets the service's key; required for a service pin, refused otherwise.</summary>
    public string? Service { get; init; }
}
