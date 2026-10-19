using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Automation.Commands;
using Wheelhouse.Application.Deployments.Automation.Models;
using Wheelhouse.Application.Deployments.Automation.Services;
using Wheelhouse.Domain.Products.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Automation.CommandHandlers;

/// <summary>Handles <see cref="ProductAutomationUpdateCommand"/>. Only a product Wheelhouse deploys takes automatic
/// deploys: turning one on for a product its own CI or nothing deploys is refused, and so is auto-promote for Wheelhouse
/// itself.</summary>
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
        if (product.Deploy != ProductDeployMode.Wheelhouse && (request.AutoDeployDev is true || request.AutoPromote is true))
            return AppResult<ProductAutomationDto>.Fail(AppErrorFactory.Conflict(product.Deploy == ProductDeployMode.Ci
                ? $"{product.Name} is deployed by its own CI; Wheelhouse never deploys it on its own."
                : $"Nothing deploys {product.Name}; set it to deploy with Wheelhouse first."));
        if (product.Slug == SelfDeploy.Constants.SelfProduct.Slug && request.AutoPromote is true)
            return AppResult<ProductAutomationDto>.Fail(AppErrorFactory.Conflict(
                "Wheelhouse never promotes itself on its own; promote it from Ready to promote with its ID typed."));

        var row = await store.SetSettingsAsync(product.Id, request.AutoDeployDev, request.AutoPromote, cancellationToken);
        return AppResult<ProductAutomationDto>.Ok(DeploymentAutomationService.Describe(product, row));
    }
}
