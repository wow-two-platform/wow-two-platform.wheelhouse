using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductPlaceCommand"/>: the runner records the job and runs it detached.</summary>
public sealed class ProductPlaceCommandHandler(
    IProductsRepository products, IServersRepository servers, IInfraGateway gateway, IInfraAccess access)
    : ICommandHandler<ProductPlaceCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(ProductPlaceCommand request, CancellationToken cancellationToken)
    {
        if (access.Refusal is { } refusal)
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal));
        if (await products.GetBySlugAsync(request.Product, cancellationToken) is null)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Product '{request.Product}' was not found."));
        if (await servers.GetBySlugAsync(request.Server, cancellationToken) is null)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Server '{request.Server}' was not found."));
        return await gateway.PlaceAsync(
            new ProductPlacementRequest(request.Product, request.Server, request.Database, request.Sites), request.Actor,
            cancellationToken);
    }
}
