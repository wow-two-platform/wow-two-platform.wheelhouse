using Wheelhouse.Application.Pins.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Pins.Queries;

/// <summary>Represents a query to get the signed-in owner's pins, in the owner's order.</summary>
public sealed record PinGetAllQuery : IQuery<AppResult<IReadOnlyList<PinDto>>>;
