namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents another deployment a detail points at.</summary>
/// <param name="Id">The submission's identifier.</param>
/// <param name="Release">Its release label.</param>
/// <param name="BundleId">Its bundle, which a deploy submits again.</param>
public sealed record DeploymentReferenceDto(string Id, string? Release, string? BundleId);
