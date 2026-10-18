namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one product's finished rollouts over a calendar's range.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="Succeeded">Rollouts that succeeded, warnings included.</param>
/// <param name="Failed">Rollouts that failed, rolled back or were interrupted.</param>
public sealed record DeploymentProductCountDto(string Product, int Succeeded, int Failed);
