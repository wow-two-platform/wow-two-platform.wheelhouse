using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Pins.Commands;
using Wheelhouse.Application.Pins.Mappers;
using Wheelhouse.Application.Pins.Models;
using Wheelhouse.Domain.Pins.Entities;
using Wheelhouse.Domain.Pins.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Pins.CommandHandlers;

/// <summary>Handles <see cref="PinCreateCommand"/>: a new product pin comes last in the owner's order; a product already
/// pinned answers its existing pin. The validator has refused every other kind.</summary>
public sealed class PinCreateCommandHandler(IPinsRepository pins, IProductsRepository products, IActorContext actorContext)
    : ICommandHandler<PinCreateCommand, AppResult<PinCreated>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<PinCreated>> HandleAsync(PinCreateCommand request, CancellationToken cancellationToken)
    {
        var product = await products.FindAsync(request.Product!, cancellationToken);
        if (product is null)
            return AppResult<PinCreated>.Fail(AppErrorFactory.Validation($"Product '{request.Product}' was not found."));

        var owner = actorContext.Actor;
        var existing = await pins.FindAsync(owner, PinKind.Product, null, product.Id, null, cancellationToken);
        if (existing is not null)
            return AppResult<PinCreated>.Ok(new PinCreated(PinMapper.Map(existing), Created: false));

        var pin = await pins.CreateAsync(new PinEntity
        {
            Id = Guid.CreateVersion7(),
            Owner = owner,
            Kind = PinKind.Product,
            ProductId = product.Id,
            Order = await pins.NextOrderAsync(owner, cancellationToken),
        }, cancellationToken);
        return AppResult<PinCreated>.Ok(new PinCreated(PinMapper.Map(pin), Created: true));
    }
}
