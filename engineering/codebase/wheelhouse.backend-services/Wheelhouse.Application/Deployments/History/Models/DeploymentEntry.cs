namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one submission joined with the inventory: its product, workspace and environment, the target's
/// previous success, and the services it changed.</summary>
/// <param name="Record">The runner's record.</param>
/// <param name="Product">The product slug: the target's product, else the one the bundle names.</param>
/// <param name="Workspace">The product's workspace slug; null for a product the inventory no longer holds.</param>
/// <param name="Environment">The target's environment; null for a removed target.</param>
/// <param name="Previous">The target's last succeeded rollout submitted before this one.</param>
/// <param name="Changes">Each service against <paramref name="Previous"/>; null when the bundle is unknown.</param>
public sealed record DeploymentEntry(
    DeploymentRecord Record,
    string? Product,
    string? Workspace,
    string? Environment,
    DeploymentRecord? Previous,
    IReadOnlyList<ServiceChangeDto>? Changes)
{
    /// <summary>Gets what the status amounts to.</summary>
    public string Outcome { get; } = DeploymentOutcome.Of(Record.Status);
}
