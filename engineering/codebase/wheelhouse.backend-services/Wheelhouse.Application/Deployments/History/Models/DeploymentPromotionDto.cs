namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a release ready for prod: the newest release that succeeded on a product's dev and is not what
/// its prod runs.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="Workspace">The product's workspace slug.</param>
/// <param name="Release">The release label.</param>
/// <param name="BundleId">The bundle a promotion submits.</param>
/// <param name="PublishedAt">When the release was published, when known.</param>
/// <param name="DevTargetId">The dev target it succeeded on.</param>
/// <param name="DevSucceededAt">When it finished there.</param>
/// <param name="ProdTargetId">The prod target to promote it to.</param>
/// <param name="ProdRelease">The release prod runs now; null when it runs none.</param>
/// <param name="RollbackCompatible">Whether a failed prod rollout can restore the previous images on its own; false
/// means it cannot roll back automatically; null when the bundle is unknown.</param>
/// <param name="HasMigrations">Whether the release declares schema migrations; null while its bundle does not say.</param>
/// <param name="Verifies">Whether the release declares an end-to-end check for dev; null when the bundle is unknown.</param>
/// <param name="Verification">The check's result on the dev rollout that proved the release; null when it ran none.
/// A manual promote shows it; auto-promote takes only a release whose check passed.</param>
public sealed record DeploymentPromotionDto(
    string Product, string? Workspace, string Release, string BundleId, DateTimeOffset? PublishedAt,
    string DevTargetId, DateTimeOffset DevSucceededAt, string ProdTargetId, string? ProdRelease,
    bool? RollbackCompatible = null, bool? HasMigrations = null, bool? Verifies = null,
    DeploymentVerificationDto? Verification = null);
