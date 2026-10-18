namespace Wheelhouse.Application.Portfolio.Models;

/// <summary>Represents one week's finished rollouts.</summary>
/// <param name="Succeeded">Rollouts that succeeded, warnings included.</param>
/// <param name="Failed">Rollouts that failed, rolled back or were interrupted.</param>
public sealed record PortfolioWeekDto(int Succeeded, int Failed);
