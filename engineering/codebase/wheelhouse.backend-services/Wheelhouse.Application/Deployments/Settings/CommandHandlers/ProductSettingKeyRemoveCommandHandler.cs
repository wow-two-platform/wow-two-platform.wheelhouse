using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Settings.Commands;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Settings.CommandHandlers;

/// <summary>Handles <see cref="ProductSettingKeyRemoveCommand"/>.</summary>
public sealed class ProductSettingKeyRemoveCommandHandler(IProductsRepository products, IProductSettingKeysRepository keys)
    : ICommandHandler<ProductSettingKeyRemoveCommand, AppResult<bool>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<bool>> HandleAsync(ProductSettingKeyRemoveCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetBySlugAsync(request.Product, cancellationToken);
        return product is not null && await keys.RemoveAsync(product.Id, request.Key, cancellationToken)
            ? AppResult<bool>.Ok(true)
            : AppResult<bool>.Fail(AppErrorFactory.NotFound($"{request.Product} has no added key {request.Key}."));
    }
}
