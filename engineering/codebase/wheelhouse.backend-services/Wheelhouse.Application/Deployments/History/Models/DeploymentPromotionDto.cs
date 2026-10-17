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
public sealed record DeploymentPromotionDto(
    string Product, string? Workspace, string Release, string BundleId, DateTimeOffset? PublishedAt,
    string DevTargetId, DateTimeOffset DevSucceededAt, string ProdTargetId, string? ProdRelease);
