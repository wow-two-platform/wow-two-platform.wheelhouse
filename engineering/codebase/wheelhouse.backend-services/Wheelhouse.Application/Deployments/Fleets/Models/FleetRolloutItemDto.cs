namespace Wheelhouse.Application.Deployments.Fleets.Models;

/// <summary>Represents one product of a fleet rollout and where it stands.</summary>
/// <param name="Position">Its place in the fleet's order, from 0.</param>
/// <param name="Product">The product's slug.</param>
/// <param name="Target">The prod target's slug.</param>
/// <param name="BundleId">The catalog bundle it promotes.</param>
/// <param name="Release">The release label, when known.</param>
/// <param name="FromRelease">The release prod ran when the fleet started; null when it ran none.</param>
/// <param name="Status"><c>waiting</c>, <c>running</c>, <c>succeeded</c>, <c>failed</c>, <c>refused</c> or
/// <c>not_started</c>.</param>
/// <param name="DeploymentId">Its deployment, once submitted.</param>
/// <param name="Detail">Why it failed or was refused, or a note on its success.</param>
/// <param name="StartedAt">When it was submitted.</param>
/// <param name="CompletedAt">When it ended.</param>
public sealed record FleetRolloutItemDto(
    int Position, string Product, string Target, string BundleId, string? Release, string? FromRelease, string Status,
    string? DeploymentId, string? Detail, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);
