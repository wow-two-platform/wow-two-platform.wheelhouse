using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Pins.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Pins.CommandHandlers;

/// <summary>Handles <see cref="PinDeleteCommand"/>; another owner's pin reads as not found.</summary>
public sealed class PinDeleteCommandHandler(IPinsRepository pins, IActorContext actorContext)
    : ICommandHandler<PinDeleteCommand, AppResult<Unit>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<Unit>> HandleAsync(PinDeleteCommand request, CancellationToken cancellationToken)
    {
        var pin = await pins.GetByIdAsync(request.Id, cancellationToken);
        if (pin is null || pin.Owner != actorContext.Actor)
            return AppResult<Unit>.Fail(AppErrorFactory.NotFound($"Pin '{request.Id}' was not found."));

        await pins.DeleteAsync(pin, cancellationToken);
        return AppResult<Unit>.Ok(default);
    }
}
