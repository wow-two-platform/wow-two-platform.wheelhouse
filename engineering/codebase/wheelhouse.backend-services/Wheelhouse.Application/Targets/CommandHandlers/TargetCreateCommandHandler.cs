using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Targets.Commands;
using Wheelhouse.Application.Targets.Mappers;
using Wheelhouse.Application.Targets.Models;
using Wheelhouse.Domain.Targets.Entities;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Cqrs;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Targets.CommandHandlers;

/// <summary>Handles <see cref="TargetCreateCommand"/>; its product and server must exist.</summary>
public sealed class TargetCreateCommandHandler(
    ITargetsRepository targets, IProductsRepository products, IServersRepository servers)
    : ICommandHandler<TargetCreateCommand, AppResult<TargetDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<TargetDto>> HandleAsync(TargetCreateCommand request, CancellationToken cancellationToken)
    {
        if (await targets.GetBySlugAsync(request.Slug, cancellationToken) is not null)
            return AppResult<TargetDto>.Fail(AppErrorFactory.Conflict($"A target is already named '{request.Slug}'."));
        var product = await products.GetBySlugAsync(request.Product, cancellationToken);
        if (product is null)
            return AppResult<TargetDto>.Fail(AppErrorFactory.Validation($"Product '{request.Product}' was not found."));
        var server = await servers.GetBySlugAsync(request.Server, cancellationToken);
        if (server is null)
            return AppResult<TargetDto>.Fail(AppErrorFactory.Validation($"Server '{request.Server}' was not found."));

        var target = await targets.CreateAsync(new TargetEntity
        {
            Id = Guid.CreateVersion7(),
            Slug = request.Slug,
            ProductId = product.Id,
            ServerId = server.Id,
            Environment = request.Environment,
            Network = request.Network,
            Root = request.Root,
            Settings = request.Settings,
            SmokeChecks = request.SmokeChecks,
            Sites = request.Sites,
        }, cancellationToken);
        return AppResult<TargetDto>.Ok(TargetMapper.Map(target, product.Slug, server.Slug));
    }
}
