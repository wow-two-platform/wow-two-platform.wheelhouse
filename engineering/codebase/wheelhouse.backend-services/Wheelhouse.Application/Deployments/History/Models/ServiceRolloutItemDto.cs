namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one service of a rollout.</summary>
/// <param name="Name">The service's key.</param>
/// <param name="Change"><c>added</c>, <c>updated</c> (its definition or image differs) or <c>unchanged</c>.</param>
/// <param name="Outcome"><c>unchanged</c> (kept running untouched), <c>pending</c>, <c>updated</c>, <c>failed</c>,
/// <c>not_started</c> (a wave before it failed) or <c>rolled_back</c>.</param>
/// <param name="DependsOn">The services it declares it depends on.</param>
public sealed record ServiceRolloutItemDto(string Name, string Change, string Outcome, IReadOnlyList<string> DependsOn);
