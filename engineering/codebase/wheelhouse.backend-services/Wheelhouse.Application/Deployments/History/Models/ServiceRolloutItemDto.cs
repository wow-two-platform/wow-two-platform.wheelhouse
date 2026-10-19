namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one service of a rollout.</summary>
/// <param name="Name">The service's key.</param>
/// <param name="Change"><c>added</c>, <c>updated</c> (its definition or image differs) or <c>unchanged</c>.</param>
/// <param name="Outcome"><c>unchanged</c> (kept running untouched), <c>pending</c>, <c>updated</c>, <c>failed</c>,
/// <c>not_started</c> (a wave before it failed) or <c>rolled_back</c>.</param>
/// <param name="DependsOn">The services it declares it depends on.</param>
/// <param name="From">The version it ran before; null when the runner did not record it.</param>
/// <param name="To">The version the release brought; null when the runner did not record it.</param>
/// <param name="Reason">Why it changed: <c>image</c>, <c>settings</c> or <c>added</c>; null when unchanged or unrecorded.</param>
/// <param name="Health">Its health when it settled, such as <c>healthy</c> or <c>unhealthy</c>; null when unrecorded.</param>
/// <param name="At">When it settled; null when unrecorded.</param>
public sealed record ServiceRolloutItemDto(
    string Name, string Change, string Outcome, IReadOnlyList<string> DependsOn, string? From = null, string? To = null,
    string? Reason = null, string? Health = null, DateTimeOffset? At = null);
