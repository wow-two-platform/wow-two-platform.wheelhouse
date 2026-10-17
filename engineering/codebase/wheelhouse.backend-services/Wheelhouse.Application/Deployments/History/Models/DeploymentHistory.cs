namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents every recorded deployment joined with the inventory, newest first, and the inventory's
/// targets.</summary>
/// <param name="Entries">Every submission, newest first (submission instant, then identifier).</param>
/// <param name="Targets">The inventory's targets.</param>
public sealed record DeploymentHistory(IReadOnlyList<DeploymentEntry> Entries, IReadOnlyList<DeploymentTargetFacts> Targets);
