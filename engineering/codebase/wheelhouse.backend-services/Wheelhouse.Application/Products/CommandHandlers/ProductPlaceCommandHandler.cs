using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Products.CommandHandlers;

/// <summary>Handles <see cref="ProductPlaceCommand"/>: the request must ask only for what the product's
/// <c>deploy.yml</c> calls for — databases when a service needs PostgreSQL, nodes for its private sites — then the
/// runner checks it against the same descriptor, records the job and runs it detached.</summary>
public sealed class ProductPlaceCommandHandler(
    IProductsRepository products,
    IServersRepository servers,
    IInfraGateway gateway,
    IInfraAccess access,
    ProductDescriptorService descriptors)
    : ICommandHandler<ProductPlaceCommand, AppResult<JsonElement>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<JsonElement>> HandleAsync(ProductPlaceCommand request, CancellationToken cancellationToken)
    {
        if (access.Refusal is { } refusal)
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict(refusal));
        if (await products.GetBySlugAsync(request.Product, cancellationToken) is not { } product)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Product '{request.Product}' was not found."));
        if (await servers.GetBySlugAsync(request.Server, cancellationToken) is null)
            return AppResult<JsonElement>.Fail(AppErrorFactory.NotFound($"Server '{request.Server}' was not found."));

        var descriptor = await descriptors.ReadAsync(product, cancellationToken);
        if (descriptor.Problem is { } problem)
            return AppResult<JsonElement>.Fail(AppErrorFactory.Conflict("A placement follows the product's deploy.yml: " + problem));
        if (Mismatch(request, descriptor) is { } mismatch)
            return AppResult<JsonElement>.Fail(AppErrorFactory.Validation(mismatch));

        return await gateway.PlaceAsync(
            new ProductPlacementRequest(request.Product, request.Server, request.Database, request.Sites, descriptor),
            request.Actor, cancellationToken);
    }

    /// <summary>Why the request asks for something the descriptor does not call for, or <c>null</c>.</summary>
    private static string? Mismatch(ProductPlaceCommand request, ProductDescriptorDto descriptor)
    {
        if (request.Database && !descriptor.NeedsPostgres)
            return $"No service of {request.Product} needs PostgreSQL, so it takes no databases.";
        foreach (var site in request.Sites)
        {
            var declared = descriptor.Sites.FirstOrDefault(item => item.Name == site.Site);
            if (declared is null)
                return $"{request.Product}'s deploy.yml declares no site '{site.Site}'.";
            if (declared.Exposure != "private")
                return $"Site '{site.Site}' is public: it answers on its baseline host and needs no tailnet node.";
        }

        return null;
    }
}
