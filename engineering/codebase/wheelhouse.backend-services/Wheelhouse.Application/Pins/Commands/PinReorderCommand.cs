using Wheelhouse.Application.Pins.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Pins.Commands;

/// <summary>Represents a command to put the signed-in operator's pins in a new order; it names every pin exactly once.</summary>
public sealed record PinReorderCommand : ICommand<AppResult<IReadOnlyList<PinDto>>>
{
    /// <summary>Gets every one of the operator's pin identifiers, first to last.</summary>
    public required IReadOnlyList<Guid> Ids { get; init; }
}
