using Wheelhouse.Application.Pins.Models;
using Wheelhouse.Domain.Pins.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Pins.Commands;

/// <summary>Represents a command to pin a product for the signed-in owner; pinning it again answers the existing pin.
/// Workspace and service pins are refused. A preference, so it is not audited.</summary>
public sealed record PinCreateCommand : ICommand<AppResult<PinCreated>>
{
    /// <summary>Gets what the pin points at; only <see cref="PinKind.Product"/> is accepted.</summary>
    public required PinKind Kind { get; init; }

    /// <summary>Gets a workspace; always refused, since workspaces are not pinned.</summary>
    public Guid? WorkspaceId { get; init; }

    /// <summary>Gets the product to pin, by identifier or slug; required.</summary>
    public string? Product { get; init; }

    /// <summary>Gets a service key; always refused, since services are not pinned.</summary>
    public string? Service { get; init; }
}
