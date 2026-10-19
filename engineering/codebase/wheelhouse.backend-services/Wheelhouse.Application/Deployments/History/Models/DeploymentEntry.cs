using Wheelhouse.Application.Deployments.History.Services;

namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one submission joined with the inventory: its product, workspace and environment, the target's
/// previous success, and the services it changed.</summary>
/// <param name="Record">The runner's record.</param>
/// <param name="Product">The product slug: the target's product, else the one the bundle names.</param>
/// <param name="Workspace">The product's workspace slug; null for a product the inventory no longer holds.</param>
/// <param name="Environment">The target's environment; for a removed target, the one the runner recorded, if any.</param>
/// <param name="Previous">The target's last succeeded rollout submitted before this one.</param>
/// <param name="Changes">Each service against <paramref name="Previous"/>; null when the bundle is unknown.</param>
/// <param name="Settings">The setting and secret keys its render changed against the target's last good render; null
/// when Wheelhouse recorded no render to compare.</param>
public sealed record DeploymentEntry(
    DeploymentRecord Record,
    string? Product,
    string? Workspace,
    string? Environment,
    DeploymentRecord? Previous,
    IReadOnlyList<ServiceChangeDto>? Changes,
    IReadOnlyList<Settings.Models.SettingChangeDto>? Settings = null)
{
    /// <summary>Gets what the status amounts to, as the API answered before the status taxonomy; kept one release for
    /// integrations.</summary>
    public string Outcome { get; } = DeploymentOutcome.Of(Record.Status);

    /// <summary>Gets the deployment in the status taxonomy: what the environment runs because of it.</summary>
    public DeploymentStateReading State { get; } = DeploymentStateRules.Of(Record, Record.Watch, Record.Intent, Previous is null);

    /// <summary>Gets whether the deployment changed nothing on its target.</summary>
    public bool NotApplied => State.State == DeploymentState.NotApplied;
}
