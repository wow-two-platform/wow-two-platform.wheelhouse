using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents what a deploy's settings render held, without values: each key's kind and a fingerprint (a
/// secret's version and keyed fingerprint, a setting's digest), and which keys changed against the target's previous
/// render. A failed deploy that changed settings points at them.</summary>
public sealed class DeploymentRenderEntity : IHasTableName
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "deployment_renders";

    /// <summary>Gets or sets the runner's job identifier; the row's key.</summary>
    public Guid JobId { get; set; }

    /// <summary>Gets or sets the target's slug.</summary>
    public required string Target { get; set; }

    /// <summary>Gets or sets the release bundle the deploy submitted.</summary>
    public string? BundleId { get; set; }

    /// <summary>Gets or sets each key the render held, as JSON: <c>{key: {"kind", "version", "fingerprint"}}</c>.</summary>
    public required string Keys { get; set; }

    /// <summary>Gets or sets the keys that changed against the previous render, as JSON: a list of
    /// <c>{"key", "kind", "change", "from", "to"}</c>; null when there was no previous render to compare.</summary>
    public string? Changes { get; set; }

    /// <summary>Gets or sets when the deploy was submitted.</summary>
    public DateTimeOffset At { get; set; }
}
