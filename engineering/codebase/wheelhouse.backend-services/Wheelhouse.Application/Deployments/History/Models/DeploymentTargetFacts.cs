using Wheelhouse.Domain.Products.Enums;

namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents an inventory target as the history reads it.</summary>
/// <param name="Slug">The target's slug, which the runner's records name.</param>
/// <param name="Product">The product's slug.</param>
/// <param name="Workspace">The product's workspace slug.</param>
/// <param name="Environment">The environment, lowercase.</param>
/// <param name="Deploy">What deploys the target's product; only a product Wheelhouse deploys is offered for
/// promotion.</param>
public sealed record DeploymentTargetFacts(
    string Slug, string Product, string? Workspace, string Environment, ProductDeployMode Deploy = ProductDeployMode.Wheelhouse);
