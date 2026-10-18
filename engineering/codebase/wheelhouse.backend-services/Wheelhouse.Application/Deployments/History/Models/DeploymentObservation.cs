namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents everything a target last reported about one rollout, read from the runner's local files.</summary>
public sealed record DeploymentObservation
{
    /// <summary>Gets the rollout's ordered steps.</summary>
    public IReadOnlyList<DeploymentStepDto>? Steps { get; init; }

    /// <summary>Gets the sites the rollout published, with each probe's outcome.</summary>
    public IReadOnlyList<DeploymentSiteDto>? Sites { get; init; }

    /// <summary>Gets what needs a look although the rollout succeeded.</summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>Gets who reconciled an interrupted rollout.</summary>
    public string? ReconciledBy { get; init; }

    /// <summary>Gets the restore point a prod rollout took before it changed anything.</summary>
    public DeploymentSnapshotDto? Snapshot { get; init; }
}
