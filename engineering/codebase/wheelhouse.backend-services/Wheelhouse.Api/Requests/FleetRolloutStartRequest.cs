using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Selects releases Ready to promote, one per prod target, to promote in one run in this order; accepts no
/// commands, credentials or paths.</summary>
/// <param name="Items">The products to promote, in order.</param>
/// <param name="Reason">The owner's one-line why, kept on every deploy and in the audit trail; needed to override a freeze.</param>
/// <param name="OverrideFreeze">Promotes although a freeze holds a target; needs <paramref name="Reason"/>.</param>
public sealed record FleetRolloutStartRequest(
    [Required, MinLength(1), MaxLength(20)] IReadOnlyList<FleetRolloutItemApiRequest> Items,
    [MaxLength(300)] string? Reason = null,
    bool OverrideFreeze = false);
