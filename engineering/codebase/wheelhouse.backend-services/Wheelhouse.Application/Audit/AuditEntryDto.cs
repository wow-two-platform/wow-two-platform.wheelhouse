using System.Text.RegularExpressions;
using Wheelhouse.Domain.Audit.Entities;
using Wheelhouse.Domain.Audit.Enums;

namespace Wheelhouse.Application.Audit;

/// <summary>One audit entry as the dashboard shows it; the chain hashes stay server-side.</summary>
/// <param name="Sequence">The entry's position in the chain.</param>
/// <param name="OccurredAt">The UTC instant the action ended.</param>
/// <param name="Actor">The operator's login.</param>
/// <param name="Action">The action's stable name.</param>
/// <param name="Subject">What the action acted on.</param>
/// <param name="Outcome">How the action ended.</param>
/// <param name="Detail">Operator-safe context.</param>
/// <param name="Reason">Why a failed action failed.</param>
public sealed partial record AuditEntryDto(
    long Sequence, DateTimeOffset OccurredAt, string Actor, string Action, string Subject, AuditOutcome Outcome,
    string? Detail, string? Reason)
{
    /// <summary>Maps a stored entry, leaving its hashes behind.</summary>
    public static AuditEntryDto From(AuditEntry entry) => new(entry.Sequence, entry.OccurredAtUtc, entry.Actor,
        entry.Action, entry.Subject, entry.Outcome, Shown(entry.Action, entry.Detail), entry.Reason);

    /// <summary>A target entry's detail names its environment (<c>{product} {environment} on {server}</c>); one that
    /// names the environment retired on 2026-10-17 reads <c>test (retired)</c>. Only the shown text changes: the stored
    /// entry, which the chain hashes, stays as it was sealed.</summary>
    private static string? Shown(string action, string? detail) =>
        action is "target.create" or "target.update" && detail is not null && RetiredTarget().IsMatch(detail)
            ? RetiredTarget().Replace(detail, "$1 test (retired) on $2")
            : detail;

    [GeneratedRegex(@"^(\S+) test on (\S+)$")]
    private static partial Regex RetiredTarget();
}
