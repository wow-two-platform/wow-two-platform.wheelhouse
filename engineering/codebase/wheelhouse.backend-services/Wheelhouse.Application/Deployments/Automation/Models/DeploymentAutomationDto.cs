namespace Wheelhouse.Application.Deployments.Automation.Models;

/// <summary>Represents what the control plane deploys on its own: each product's choices and the waiting deploys.</summary>
/// <param name="Products">Every product's automation choices, by slug.</param>
/// <param name="Queue">The automatic deploys waiting for their targets, oldest first.</param>
public sealed record DeploymentAutomationDto(IReadOnlyList<ProductAutomationDto> Products, IReadOnlyList<QueuedDeploymentDto> Queue);
