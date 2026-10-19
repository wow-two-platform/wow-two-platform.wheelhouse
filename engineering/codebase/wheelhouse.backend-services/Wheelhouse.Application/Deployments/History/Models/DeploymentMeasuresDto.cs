namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents outcome counts, success rate, and rollout and recovery times over one range, with the deploys
/// that changed nothing and the attention the rest need.</summary>
/// <param name="From">The range's start, inclusive.</param>
/// <param name="To">The range's end, exclusive.</param>
/// <param name="Deploys">Every submission in the range.</param>
/// <param name="Succeeded">Rollouts that succeeded.</param>
/// <param name="Failed">Rollouts that failed, rolled back or were interrupted.</param>
/// <param name="Refused">Submissions the runner refused before changing anything.</param>
/// <param name="Pending">Submissions still running or without a receipt.</param>
/// <param name="SuccessRate">The succeeded share of finished rollouts; null without one.</param>
/// <param name="MedianRolloutSeconds">The median time from start to a finished outcome.</param>
/// <param name="MedianRecoverySeconds">The median time from a target's failure to its next success.</param>
/// <param name="NotApplied">Deploys that changed nothing: refused, replaced, cancelled, expired, a failed check or
/// stopped before changes. The range's own measures count them whether or not the other figures include them.</param>
/// <param name="Look">Deploys worth a look, those that changed nothing included.</param>
/// <param name="Act">Deploys that need action.</param>
public sealed record DeploymentMeasuresDto(
    DateTimeOffset From, DateTimeOffset To, int Deploys, int Succeeded, int Failed, int Refused, int Pending,
    double? SuccessRate, double? MedianRolloutSeconds, double? MedianRecoverySeconds, int NotApplied = 0, int Look = 0,
    int Act = 0);
