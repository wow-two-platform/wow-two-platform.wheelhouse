using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Deployments.Automation.Models;

/// <summary>Represents what the control plane deploys of one product on its own.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="AutoDeployDev">Whether each new published release deploys to the product's dev targets.</param>
/// <param name="AutoPromote">Whether a release that passed its dev watch window is promoted to prod.</param>
/// <param name="CiDeployed">Whether the product's own CI deploys it, so the control plane never does.</param>
/// <param name="Deploy">What deploys the product; only <see cref="ProductDeployMode.Wheelhouse"/> takes automatic
/// deploys.</param>
public sealed record ProductAutomationDto(
    string Product, bool AutoDeployDev, bool AutoPromote, bool CiDeployed, ProductDeployMode Deploy);
