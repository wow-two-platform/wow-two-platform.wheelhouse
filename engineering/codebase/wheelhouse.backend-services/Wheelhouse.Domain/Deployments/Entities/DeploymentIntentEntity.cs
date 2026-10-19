using Wheelhouse.Domain.Deployments.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents a deploy that changed nothing because it never reached its target: refused before submission,
/// replaced in the queue by a newer release, cancelled, expired or aborted. The runner never hears of it, so the control
/// plane keeps it, and the deployment history joins it beside the runner's records as a Not applied deploy.</summary>
/// <remarks>Targets and products are named by slug, as the runner's records name them, so a row outlives its target.</remarks>
public sealed class DeploymentIntentEntity : IKeyedEntity<Guid>, IHasTableName
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "deployment_intents";

    /// <summary>Gets or sets the intent's identifier, which the history lists it by.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the target's slug.</summary>
    public required string Target { get; set; }

    /// <summary>Gets or sets the product's slug, when the inventory knew it.</summary>
    public string? Product { get; set; }

    /// <summary>Gets or sets the target's environment, lowercase, when the inventory knew it.</summary>
    public string? Environment { get; set; }

    /// <summary>Gets or sets the release bundle it would have submitted.</summary>
    public string? BundleId { get; set; }

    /// <summary>Gets or sets the release label.</summary>
    public string? Release { get; set; }

    /// <summary>Gets or sets who asked for it: a login, <c>key:{name}</c> or the automation's actor.</summary>
    public required string Actor { get; set; }

    /// <summary>Gets or sets how it ended.</summary>
    public DeploymentIntentOutcome Outcome { get; set; }

    /// <summary>Gets or sets what refused it, for a refusal.</summary>
    public DeploymentRefusal? Refusal { get; set; }

    /// <summary>Gets or sets the display-safe reason: a freeze's scope, a gate's or the runner's static rule text.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets the release that took its place, for a superseded one.</summary>
    public string? ReplacedBy { get; set; }

    /// <summary>Gets or sets why the control plane meant to deploy it on its own; null for an owner's deploy.</summary>
    public DeploymentCause? Cause { get; set; }

    /// <summary>Gets or sets the fleet rollout it belonged to.</summary>
    public Guid? FleetId { get; set; }

    /// <summary>Gets or sets when it ended.</summary>
    public DateTimeOffset At { get; set; }
}
