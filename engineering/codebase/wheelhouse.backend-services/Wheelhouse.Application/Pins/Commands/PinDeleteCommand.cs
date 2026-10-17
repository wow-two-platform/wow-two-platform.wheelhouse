using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Pins.Commands;

/// <summary>Represents a command to remove one of the signed-in operator's pins.</summary>
public sealed record PinDeleteCommand : ICommand<AppResult<Unit>>
{
    /// <summary>Gets the pin's identifier.</summary>
    public required Guid Id { get; init; }
}
