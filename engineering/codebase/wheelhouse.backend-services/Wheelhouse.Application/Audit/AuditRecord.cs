using Wheelhouse.Domain.Audit.Enums;

namespace Wheelhouse.Application.Audit;

/// <summary>One finished action, before the audit trail stamps its time and chains it.</summary>
/// <param name="Actor">The actor: the signed-in user's login, or <c>key:{name}</c> for an integration key.</param>
/// <param name="Action">The action's stable name.</param>
/// <param name="Subject">What the action acted on.</param>
/// <param name="Outcome">How the action ended.</param>
/// <param name="Detail">Display-safe context.</param>
/// <param name="Reason">Why a failed action failed.</param>
public sealed record AuditRecord(
    string Actor, string Action, string Subject, AuditOutcome Outcome, string? Detail, string? Reason);
