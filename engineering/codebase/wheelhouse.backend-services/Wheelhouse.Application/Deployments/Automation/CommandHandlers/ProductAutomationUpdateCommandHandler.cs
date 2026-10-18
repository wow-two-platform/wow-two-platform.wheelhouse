using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Automation.Commands;
using Wheelhouse.Application.Deployments.Automation.Constants;
using Wheelhouse.Application.Deployments.Automation.Models;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Automation.CommandHandlers;

/// <summary>Handles <see cref="ProductAutomationUpdateCommand"/>. A product its own CI deploys never takes automatic
/// deploys.</summary>
public sealed class ProductAutomationUpdateCommandHandler(IProductsRepository products, IDeploymentAutomationStore store)
    : ICommandHandler<ProductAutomationUpdateCommand, AppResult<ProductAutomationDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ProductAutomationDto>> HandleAsync(
        ProductAutomationUpdateCommand request, CancellationToken cancellationToken)
    {
        var product = await products.FindAsync(request.Product, cancellationToken);
        if (product is null)
            return AppResult<ProductAutomationDto>.Fail(AppErrorFactory.NotFound($"Product '{request.Product}' was not found."));
        var ci = AutomationConstants.CiDeployedProducts.Contains(product.Slug);
        if (ci && (request.AutoDeployDev is true || request.AutoPromote is true))
            return AppResult<ProductAutomationDto>.Fail(AppErrorFactory.Conflict(
                $"{product.Name} is deployed by its own CI; Wheelhouse never deploys it on its own."));

        var row = await store.SetSettingsAsync(product.Id, request.AutoDeployDev, request.AutoPromote, cancellationToken);
        return AppResult<ProductAutomationDto>.Ok(new ProductAutomationDto(product.Slug, row.AutoDeployDev && !ci,
            row.AutoPromote && !ci, ci));
    }
}
