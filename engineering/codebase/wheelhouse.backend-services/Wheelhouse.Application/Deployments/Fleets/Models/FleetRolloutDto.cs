namespace Wheelhouse.Application.Deployments.Fleets.Models;

/// <summary>Represents a fleet rollout: several products promoted to prod in one run, in order, stopping at the first
/// failure.</summary>
/// <param name="Id">The fleet's identifier.</param>
/// <param name="Actor">Who started it.</param>
/// <param name="Reason">The owner's one-line why, kept on every deploy.</param>
/// <param name="OverrideFreeze">Whether its deploys go through a freeze.</param>
/// <param name="Status"><c>running</c>, <c>succeeded</c>, <c>failed</c> or <c>cancelled</c>.</param>
/// <param name="CreatedAt">When it was started.</param>
/// <param name="CompletedAt">When it ended; null while it runs.</param>
/// <param name="CancelRequestedBy">Who asked to cancel it, once someone did.</param>
/// <param name="CancelRequestedAt">When cancelling was asked for; it stops after the product it is on.</param>
/// <param name="Items">Its products, in order.</param>
public sealed record FleetRolloutDto(
    Guid Id, string Actor, string? Reason, bool OverrideFreeze, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, string? CancelRequestedBy, DateTimeOffset? CancelRequestedAt,
    IReadOnlyList<FleetRolloutItemDto> Items);
