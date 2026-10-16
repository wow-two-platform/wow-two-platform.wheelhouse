using Wheelhouse.Domain.Audit.Entities;
using WoW.Two.Sdk.Beta.Be.Foundation.Audit;

namespace Wheelhouse.Persistence.Audit;

/// <summary>Names the audit fields the hash chain proves, in order. Changing the set starts a new scheme version;
/// keep this one for the entries it already sealed.</summary>
public sealed class AuditEntryCanonicalizer : IChainedEntryCanonicalizer<AuditEntry>
{
    /// <inheritdoc />
    public int SchemeVersion => 1;

    /// <inheritdoc />
    public void Write(AuditEntry entry, ICanonicalPayloadBuilder builder)
    {
        builder.Append(entry.Id.ToString("D"));
        builder.Append(entry.OccurredAtUtc);
        builder.Append(entry.Actor);
        builder.Append(entry.Action);
        builder.Append(entry.Subject);
        builder.Append((int)entry.Outcome);
        Append(builder, entry.Detail);
        Append(builder, entry.Reason);
    }

    private static void Append(ICanonicalPayloadBuilder builder, string? value)
    {
        if (value is null)
            builder.AppendNull();
        else
            builder.Append(value);
    }
}
