namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one calendar day's deployments, in the owner's time zone: the finished rollouts that changed the
/// environment by the attention they need, and the deploys that changed nothing beside them.</summary>
/// <param name="Date">The local date.</param>
/// <param name="Succeeded">Applied rollouts that succeeded, degraded ones included; the old outcome count.</param>
/// <param name="Failed">Applied rollouts that failed, rolled back or were interrupted; the old outcome count.</param>
/// <param name="Calm">Applied rollouts that need nothing.</param>
/// <param name="Look">Applied rollouts worth a look: degraded or rolled back.</param>
/// <param name="Act">Applied rollouts that need action: partial or failed.</param>
/// <param name="NotApplied">Deploys that changed nothing; they never colour the day.</param>
public sealed record DeploymentDayDto(DateOnly Date, int Succeeded, int Failed, int Calm = 0, int Look = 0, int Act = 0, int NotApplied = 0);
