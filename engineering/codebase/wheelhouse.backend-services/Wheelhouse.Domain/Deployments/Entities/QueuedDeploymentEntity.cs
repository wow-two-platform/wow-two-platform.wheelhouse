using Wheelhouse.Domain.Deployments.Enums;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents the one automatic deploy waiting for a target, latest wins: a newer release replaces an older
/// one, and it waits for the target's running rollout to finish.</summary>
public sealed class QueuedDeploymentEntity : IHasTableName
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "deployment_queue";

    /// <summary>Gets or sets the target the release waits for; the row's key.</summary>
    public Guid TargetId { get; set; }

    /// <summary>Gets or sets the catalog bundle the deploy submits.</summary>
    public required string BundleId { get; set; }

    /// <summary>Gets or sets the release label.</summary>
    public required string Release { get; set; }

    /// <summary>Gets or sets when the release was published; a later one replaces this one.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Gets or sets why the deploy was queued.</summary>
    public DeploymentCause Cause { get; set; }

    /// <summary>Gets or sets when it was queued.</summary>
    public DateTimeOffset QueuedAt { get; set; }
}
