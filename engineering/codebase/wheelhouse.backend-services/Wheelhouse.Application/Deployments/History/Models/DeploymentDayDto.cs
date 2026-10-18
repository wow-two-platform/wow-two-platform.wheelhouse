namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one calendar day's finished rollouts, in the owner's time zone.</summary>
/// <param name="Date">The local date.</param>
/// <param name="Succeeded">Rollouts that succeeded, warnings included.</param>
/// <param name="Failed">Rollouts that failed, rolled back or were interrupted.</param>
public sealed record DeploymentDayDto(DateOnly Date, int Succeeded, int Failed);
