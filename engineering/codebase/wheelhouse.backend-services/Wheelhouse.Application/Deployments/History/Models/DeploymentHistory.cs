namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents every recorded deployment joined with the inventory, newest first, and the inventory's
/// targets.</summary>
/// <param name="Entries">Every deploy, newest first (submission instant, then identifier).</param>
/// <param name="Targets">The inventory's targets.</param>
public sealed record DeploymentHistory(IReadOnlyList<DeploymentEntry> Entries, IReadOnlyList<DeploymentTargetFacts> Targets)
{
    /// <summary>Gets every control — a stop, start or restart of the release a target runs — newest first. Kept apart
    /// from <see cref="Entries"/>: a control is no rollout, so no reading of rollouts weighs it.</summary>
    public IReadOnlyList<DeploymentEntry> Controls { get; init; } = [];

    /// <summary>Gets what the owner keeps stopped, by target slug, from each target's newest finished control; a
    /// target with nothing stopped is left out.</summary>
    public IReadOnlyDictionary<string, TargetStopDto> Stops { get; init; } = new Dictionary<string, TargetStopDto>();
}
