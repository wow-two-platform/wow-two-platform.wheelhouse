using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using Wheelhouse.Domain.Deployments.Enums;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>One attempt to ship a product's images to a server, with streamed logs and outcome.</summary>
public sealed class Deployment : IKeyedEntity<Guid>, IHasTableName, IAuditable
{
    /// <summary>Gets the storage table name — the single source of truth shared by EF mapping and hand-written SQL.</summary>
    public static string TableName => "deployments";

    /// <summary>Gets the deployment's unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the product being deployed.</summary>
    public required Guid ProductId { get; init; }

    /// <summary>Gets the target server.</summary>
    public required Guid ServerId { get; init; }

    /// <summary>Gets the environment name (e.g. <c>prod</c>, <c>staging</c>).</summary>
    public string Environment { get; init; } = "prod";

    /// <summary>Gets or sets the frontend image tag (git SHA) deployed.</summary>
    public string? ImageWebTag { get; set; }

    /// <summary>Gets or sets the backend image tag (git SHA) deployed.</summary>
    public string? ImageApiTag { get; set; }

    /// <summary>Gets or sets the job status.</summary>
    public DeploymentStatus Status { get; set; } = DeploymentStatus.Queued;

    /// <summary>Gets or sets the captured stdout (also streamed live over SignalR).</summary>
    public string? Log { get; set; }

    /// <summary>Gets or sets who/what triggered the deployment.</summary>
    public string? TriggeredBy { get; set; }

    /// <summary>Gets or sets the UTC instant the deployment was queued. Stamped by the SDK audit interceptor on insert
    /// (column <c>created_at_utc</c>); never hand-set.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC instant of the last change. Stamped by the SDK audit interceptor on insert and update
    /// (column <c>updated_at_utc</c>); never hand-set.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the UTC instant the deployment finished. Domain data (set when the job ends), not an audit field.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
