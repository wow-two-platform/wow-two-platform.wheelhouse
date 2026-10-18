namespace Wheelhouse.Application.Deployments.Automation.Models;

/// <summary>Represents an automatic deploy waiting for its target.</summary>
/// <param name="TargetId">The target's slug.</param>
/// <param name="Product">The target's product slug.</param>
/// <param name="Environment">The target's environment.</param>
/// <param name="Release">The release label.</param>
/// <param name="BundleId">The catalog bundle it submits.</param>
/// <param name="PublishedAt">When the release was published.</param>
/// <param name="Cause">Why it was queued: <c>auto_dev</c> or <c>auto_promote</c>.</param>
/// <param name="QueuedAt">When it was queued.</param>
public sealed record QueuedDeploymentDto(
    string TargetId, string Product, string Environment, string Release, string BundleId, DateTimeOffset? PublishedAt,
    string Cause, DateTimeOffset QueuedAt);
