namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a deployment in the status taxonomy: its state, the way it got there, the attention it needs, why,
/// what else it hurt and how each service ended.</summary>
/// <param name="State">One of <see cref="DeploymentState"/>'s names.</param>
/// <param name="Substate">Which way it got there, such as <c>partly_rolled_back</c>; null when nothing narrower holds.</param>
/// <param name="Attention">One of <see cref="DeploymentAttention"/>'s names.</param>
/// <param name="Cause">Why it ended so; null for a calm outcome.</param>
/// <param name="Effects">The services the origin likely hurt.</param>
/// <param name="Services">Each service's result in rollout order; null when the target did not report them.</param>
public sealed record DeploymentStateReading(
    string State, string? Substate, string Attention, DeploymentCauseDto? Cause, IReadOnlyList<DeploymentEffectDto> Effects,
    IReadOnlyList<DeploymentServiceResultDto>? Services);
