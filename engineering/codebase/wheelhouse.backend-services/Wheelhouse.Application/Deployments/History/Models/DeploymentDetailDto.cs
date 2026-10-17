namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one deployment in full: the list item, each service from and to, the rollout's steps, sites and
/// warnings, and what a roll back would restore.</summary>
/// <param name="Deployment">The deployment as lists show it.</param>
/// <param name="Services">Each service against what the target ran before; null when the bundle is unknown.</param>
/// <param name="Previous">The target's previous success.</param>
/// <param name="Rollback">The release a roll back restores; null unless this is what the target runs, its release
/// permits the previous images and a previous release exists.</param>
/// <param name="Steps">The rollout's steps as the target last reported them.</param>
/// <param name="Sites">The sites it published, with each probe's outcome.</param>
/// <param name="ReconciledBy">Who reconciled it, when it was interrupted.</param>
/// <param name="Observed">Whether the target's report could be read; false leaves steps and sites empty.</param>
public sealed record DeploymentDetailDto(
    DeploymentItemDto Deployment,
    IReadOnlyList<ServiceChangeDto>? Services,
    DeploymentReferenceDto? Previous,
    DeploymentReferenceDto? Rollback,
    IReadOnlyList<DeploymentStepDto> Steps,
    IReadOnlyList<DeploymentSiteDto> Sites,
    string? ReconciledBy,
    bool Observed);
