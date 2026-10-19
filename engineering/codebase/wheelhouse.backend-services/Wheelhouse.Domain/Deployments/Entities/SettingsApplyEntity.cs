using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace Wheelhouse.Domain.Deployments.Entities;

/// <summary>Represents a scheduled apply of a target's edited settings and secrets: at <see cref="At"/>, the target's
/// running release deploys again with the values of that moment, restarting only the services whose settings changed.
/// One per target; a newer schedule replaces it.</summary>
public sealed class SettingsApplyEntity : IHasTableName, ICreationAuditable
{
    /// <summary>Gets the storage table name, shared by the EF mapping and the SQL migrations.</summary>
    public static string TableName => "settings_applies";

    /// <summary>Gets or sets the target; the row's key.</summary>
    public Guid TargetId { get; set; }

    /// <summary>Gets or sets when the apply runs.</summary>
    public DateTimeOffset At { get; set; }

    /// <summary>Gets or sets who scheduled it.</summary>
    public required string RequestedBy { get; set; }

    /// <summary>Gets or sets the owner's one-line why, kept on the deploy it submits.</summary>
    public string? Reason { get; set; }

    /// <summary>Gets or sets when it was scheduled; stamped by the SDK audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
