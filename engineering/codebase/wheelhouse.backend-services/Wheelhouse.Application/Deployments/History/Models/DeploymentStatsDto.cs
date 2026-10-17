namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a range's measures beside the same measures for the range of equal length before it.</summary>
/// <param name="Current">The requested range.</param>
/// <param name="Previous">The range before it; null when it holds no submission.</param>
public sealed record DeploymentStatsDto(DeploymentMeasuresDto Current, DeploymentMeasuresDto? Previous);
