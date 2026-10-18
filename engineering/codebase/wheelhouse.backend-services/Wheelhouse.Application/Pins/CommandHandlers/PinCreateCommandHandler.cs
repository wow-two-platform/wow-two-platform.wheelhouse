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

/// <summary>Handles <see cref="PinCreateCommand"/>: a new pin comes last in the owner's order; a target already pinned
/// answers its existing pin. A service key is taken as given, since the repository declares a product's services.</summary>
public sealed class PinCreateCommandHandler(
    IPinsRepository pins, IWorkspacesRepository workspaces, IProductsRepository products, IActorContext actorContext)
    : ICommandHandler<PinCreateCommand, AppResult<PinCreated>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<PinCreated>> HandleAsync(PinCreateCommand request, CancellationToken cancellationToken)
    {
        Guid? productId = null;
        if (request.Kind == PinKind.Workspace)
        {
            if (!await workspaces.ExistsAsync(request.WorkspaceId!.Value, cancellationToken))
                return AppResult<PinCreated>.Fail(AppErrorFactory.Validation($"Workspace '{request.WorkspaceId}' was not found."));
        }
        else
        {
            var product = await products.FindAsync(request.Product!, cancellationToken);
            if (product is null)
                return AppResult<PinCreated>.Fail(AppErrorFactory.Validation($"Product '{request.Product}' was not found."));
            productId = product.Id;
        }

        var owner = actorContext.Actor;
        var existing = await pins.FindAsync(owner, request.Kind, request.WorkspaceId, productId, request.Service, cancellationToken);
        if (existing is not null)
            return AppResult<PinCreated>.Ok(new PinCreated(PinMapper.Map(existing), Created: false));

        var pin = await pins.CreateAsync(new PinEntity
        {
            Id = Guid.CreateVersion7(),
            Owner = owner,
            Kind = request.Kind,
            WorkspaceId = request.WorkspaceId,
            ProductId = productId,
            Service = request.Service,
            Order = await pins.NextOrderAsync(owner, cancellationToken),
        }, cancellationToken);
        return AppResult<PinCreated>.Ok(new PinCreated(PinMapper.Map(pin), Created: true));
    }
}
