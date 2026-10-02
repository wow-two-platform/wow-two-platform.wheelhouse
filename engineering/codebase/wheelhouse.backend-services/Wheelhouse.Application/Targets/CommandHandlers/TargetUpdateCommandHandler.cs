using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Targets.Commands;
using Wheelhouse.Application.Targets.Mappers;
using Wheelhouse.Application.Targets.Models;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Targets.CommandHandlers;

/// <summary>Handles <see cref="TargetUpdateCommand"/>; its product and server must exist.</summary>
public sealed class TargetUpdateCommandHandler(
    ITargetsRepository targets, IProductsRepository products, IServersRepository servers)
    : ICommandHandler<TargetUpdateCommand, AppResult<TargetDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<TargetDto>> HandleAsync(TargetUpdateCommand request, CancellationToken cancellationToken)
    {
        var target = await targets.GetBySlugAsync(request.Slug, cancellationToken);
        if (target is null)
            return AppResult<TargetDto>.Fail(AppErrorFactory.NotFound($"Target '{request.Slug}' was not found."));
        var product = await products.GetBySlugAsync(request.Product, cancellationToken);
        if (product is null)
            return AppResult<TargetDto>.Fail(AppErrorFactory.Validation($"Product '{request.Product}' was not found."));
        var server = await servers.GetBySlugAsync(request.Server, cancellationToken);
        if (server is null)
            return AppResult<TargetDto>.Fail(AppErrorFactory.Validation($"Server '{request.Server}' was not found."));

        target.ProductId = product.Id;
        target.ServerId = server.Id;
        target.Environment = request.Environment;
        target.Network = request.Network;
        target.Root = request.Root;
        target.Settings = request.Settings;
        target.SmokeChecks = request.SmokeChecks;
        target.Sites = request.Sites;
        await targets.UpdateAsync(target, cancellationToken);
        return AppResult<TargetDto>.Ok(TargetMapper.Map(target, product.Slug, server.Slug));
    }
}
