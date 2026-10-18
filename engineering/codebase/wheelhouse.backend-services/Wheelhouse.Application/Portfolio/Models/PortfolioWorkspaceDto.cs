namespace Wheelhouse.Application.Portfolio.Models;

/// <summary>Represents one workspace's finished rollouts over the summary's weeks, for its success rate and trend.</summary>
/// <param name="Workspace">The workspace's slug.</param>
/// <param name="Succeeded">Rollouts that succeeded, warnings included.</param>
/// <param name="Failed">Rollouts that failed, rolled back or were interrupted.</param>
/// <param name="Weekly">The same per week, aligned with <see cref="PortfolioSummaryDto.Weeks"/>.</param>
public sealed record PortfolioWorkspaceDto(string Workspace, int Succeeded, int Failed, IReadOnlyList<PortfolioWeekDto> Weekly);
