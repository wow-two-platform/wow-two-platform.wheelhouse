using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Pins.Commands;
using Wheelhouse.Application.Pins.Mappers;
using Wheelhouse.Application.Pins.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Pins.CommandHandlers;

/// <summary>Handles <see cref="PinReorderCommand"/>: the list must name every one of the owner's pins exactly once, so
/// an order read before a pin came or went is refused rather than half-applied.</summary>
public sealed class PinReorderCommandHandler(IPinsRepository pins, IActorContext actorContext)
    : ICommandHandler<PinReorderCommand, AppResult<IReadOnlyList<PinDto>>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<PinDto>>> HandleAsync(PinReorderCommand request, CancellationToken cancellationToken)
    {
        var owner = actorContext.Actor;
        var stored = await pins.GetForOwnerAsync(owner, cancellationToken);
        if (!stored.Select(pin => pin.Id).ToHashSet().SetEquals(request.Ids))
            return AppResult<IReadOnlyList<PinDto>>.Fail(AppErrorFactory.Conflict(
                "The order must name every pin exactly once; reload the pins and try again."));

        await pins.ReorderAsync(owner, request.Ids, cancellationToken);
        return AppResult<IReadOnlyList<PinDto>>.Ok([.. (await pins.GetForOwnerAsync(owner, cancellationToken)).Select(PinMapper.Map)]);
    }
}
