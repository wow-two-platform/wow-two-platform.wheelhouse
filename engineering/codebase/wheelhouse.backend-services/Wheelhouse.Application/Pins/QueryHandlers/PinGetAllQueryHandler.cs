using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Pins.Mappers;
using Wheelhouse.Application.Pins.Models;
using Wheelhouse.Application.Pins.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Pins.QueryHandlers;

/// <summary>Handles <see cref="PinGetAllQuery"/>; the owner sees only their own pins.</summary>
public sealed class PinGetAllQueryHandler(IPinsRepository pins, IActorContext actorContext)
    : IQueryHandler<PinGetAllQuery, AppResult<IReadOnlyList<PinDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<PinDto>>> HandleAsync(PinGetAllQuery request, CancellationToken cancellationToken) =>
        AppResult<IReadOnlyList<PinDto>>.Ok([.. (await pins.GetForOwnerAsync(actorContext.Actor, cancellationToken)).Select(PinMapper.Map)]);
}
