namespace Wheelhouse.Application.Portfolio.Models;

/// <summary>Represents the Products page's deployment read: each product's environments with their latest and serving
/// rollouts and weekly rollout counts, and each workspace's outcomes per week.</summary>
/// <param name="TimeZone">The IANA time zone weeks are counted in.</param>
/// <param name="Weeks">Each week's Monday, oldest first; the last is the current week.</param>
/// <param name="Products">Every product with a target or a recorded rollout, by slug.</param>
/// <param name="Workspaces">Every workspace with a target or a recorded rollout, by slug.</param>
public sealed record PortfolioSummaryDto(
    string TimeZone, IReadOnlyList<DateOnly> Weeks, IReadOnlyList<PortfolioProductDto> Products,
    IReadOnlyList<PortfolioWorkspaceDto> Workspaces);
