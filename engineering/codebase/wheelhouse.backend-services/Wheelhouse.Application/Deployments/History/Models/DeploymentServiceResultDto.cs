namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents how one service ended in a deployment, in rollout order.</summary>
/// <param name="Service">The service's key.</param>
/// <param name="Result"><c>updated</c>, <c>new</c>, <c>unchanged</c>, <c>not_reached</c>, <c>restored</c>, <c>failed</c>,
/// <c>affected</c>, <c>waiting</c>, <c>starting</c> or <c>unknown</c>.</param>
/// <param name="Origin">Whether the deployment's trouble started at this service.</param>
/// <param name="From">The version it ran before, when known.</param>
/// <param name="To">The version the release brought, when known.</param>
/// <param name="Health">Its health as the target last reported it, when the runner recorded it.</param>
/// <param name="Reason">Why it changed: <c>added</c>, <c>image</c>, <c>definition</c> or <c>settings</c>; null when
/// unchanged or unrecorded.</param>
public sealed record DeploymentServiceResultDto(
    string Service, string Result, bool Origin = false, string? From = null, string? To = null, string? Health = null,
    string? Reason = null);
