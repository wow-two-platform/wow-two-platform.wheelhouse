using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Freezes.Commands;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Freezes.CommandHandlers;

/// <summary>Handles <see cref="DeployFreezeDeleteCommand"/>; answers whether a freeze was lifted.</summary>
public sealed class DeployFreezeDeleteCommandHandler(IDeployFreezesRepository freezes, IProductsRepository products)
    : ICommandHandler<DeployFreezeDeleteCommand, AppResult<bool>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<bool>> HandleAsync(DeployFreezeDeleteCommand request, CancellationToken cancellationToken)
    {
        Guid? productId = null;
        if (request.Product is not null)
        {
            var product = await products.FindAsync(request.Product, cancellationToken);
            if (product is null)
                return AppResult<bool>.Fail(AppErrorFactory.NotFound($"Product '{request.Product}' was not found."));
            productId = product.Id;
        }

        DeploymentEnvironment? environment = request.Environment is null
            ? null
            : Enum.Parse<DeploymentEnvironment>(request.Environment, ignoreCase: true);
        var existing = await freezes.FindAsync(productId, environment, cancellationToken);
        if (existing is not null)
            await freezes.RemoveAsync(existing.Id, cancellationToken);
        return AppResult<bool>.Ok(existing is not null);
    }
}
