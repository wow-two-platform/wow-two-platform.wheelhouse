namespace Wheelhouse.Application.Deployments.Automation.Models;

/// <summary>Represents a product's newest published release, as the release watcher reads it.</summary>
/// <param name="Product">The product's slug.</param>
/// <param name="BundleId">The catalog bundle a deploy submits.</param>
/// <param name="Release">The release label.</param>
/// <param name="PublishedAt">When it was published.</param>
public sealed record LatestRelease(string Product, string BundleId, string Release, DateTimeOffset? PublishedAt);
