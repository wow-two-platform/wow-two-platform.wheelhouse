using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Freezes.Commands;
using Wheelhouse.Application.Deployments.Freezes.Models;
using Wheelhouse.Application.Deployments.Freezes.Services;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Freezes.CommandHandlers;

/// <summary>Handles <see cref="DeployFreezeCreateCommand"/>. Freezing a scope already frozen answers its freeze as it is.</summary>
public sealed class DeployFreezeCreateCommandHandler(IDeployFreezesRepository freezes, IProductsRepository products, TimeProvider time)
    : ICommandHandler<DeployFreezeCreateCommand, AppResult<DeployFreezeDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeployFreezeDto>> HandleAsync(DeployFreezeCreateCommand request, CancellationToken cancellationToken)
    {
        string? slug = null;
        Guid? productId = null;
        if (request.Product is not null)
        {
            var product = await products.FindAsync(request.Product, cancellationToken);
            if (product is null)
                return AppResult<DeployFreezeDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Product}' was not found."));
            (slug, productId) = (product.Slug, product.Id);
        }

        DeploymentEnvironment? environment = request.Environment is null
            ? null
            : Enum.Parse<DeploymentEnvironment>(request.Environment, ignoreCase: true);
        var existing = await freezes.FindAsync(productId, environment, cancellationToken);
        if (existing is not null)
            return AppResult<DeployFreezeDto>.Ok(DeployFreezeService.Map(existing, slug));

        var stored = await freezes.AddAsync(new DeployFreezeEntity
        {
            Id = Guid.NewGuid(), ProductId = productId, Environment = environment,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            Actor = request.Actor, CreatedAt = time.GetUtcNow(),
        }, cancellationToken);
        return AppResult<DeployFreezeDto>.Ok(DeployFreezeService.Map(stored, slug));
    }
}
