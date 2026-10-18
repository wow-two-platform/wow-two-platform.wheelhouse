using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Foundation.Audit;
using Wheelhouse.Domain.Audit.Enums;

namespace Wheelhouse.Domain.Audit.Entities;

/// <summary>One action in the append-only audit trail. Each entry is hash-chained to the one before it, so
/// an edited or removed entry breaks verification. Every field is display-safe text: never a secret value or token.</summary>
public sealed class AuditEntry : IKeyedEntity<Guid>, IHasTableName, IHashChainedEntry
{
    /// <summary>Gets the storage table name — the single source of truth shared by EF mapping and hand-written SQL.</summary>
    public static string TableName => "audit_entries";

    /// <summary>Gets the entry's unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets or sets the entry's position in the chain, starting at 1. Stamped by the chain sealer.</summary>
    public long Sequence { get; set; }

    /// <summary>Gets or sets the previous entry's hash; empty for the first entry. Stamped by the chain sealer.</summary>
    public byte[] PreviousHash { get; set; } = [];

    /// <summary>Gets or sets this entry's hash over its fields and <see cref="PreviousHash"/>. Stamped by the chain sealer.</summary>
    public byte[] Hash { get; set; } = [];

    /// <summary>Gets or sets the UTC instant the action ended.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Gets or sets the actor: the signed-in user's login, or <c>key:{name}</c> for an integration key. Stored
    /// and hashed as <c>actor</c>.</summary>
    public required string Actor { get; set; }

    /// <summary>Gets or sets the action's stable name, such as <c>deployment.start</c>.</summary>
    public required string Action { get; set; }

    /// <summary>Gets or sets what the action acted on: a target, a product or a vault path.</summary>
    public required string Subject { get; set; }

    /// <summary>Gets or sets how the action ended.</summary>
    public AuditOutcome Outcome { get; set; }

    /// <summary>Gets or sets display-safe context, such as the release a deployment took.</summary>
    public string? Detail { get; set; }

    /// <summary>Gets or sets why a failed action failed, as the owner saw it.</summary>
    public string? Reason { get; set; }
}
