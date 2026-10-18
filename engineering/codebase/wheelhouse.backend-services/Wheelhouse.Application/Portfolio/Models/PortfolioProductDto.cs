namespace Wheelhouse.Application.Portfolio.Models;

/// <summary>Represents one product's deployment summary over the summary's weeks.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="Workspace">The product's workspace slug.</param>
/// <param name="Succeeded">Rollouts that succeeded within the weeks, warnings included.</param>
/// <param name="Failed">Rollouts that failed, rolled back or were interrupted within the weeks.</param>
/// <param name="Environments">Each environment the product has a target in or deployed to, dev before prod.</param>
public sealed record PortfolioProductDto(
    string Product, string? Workspace, int Succeeded, int Failed, IReadOnlyList<PortfolioEnvironmentDto> Environments);
