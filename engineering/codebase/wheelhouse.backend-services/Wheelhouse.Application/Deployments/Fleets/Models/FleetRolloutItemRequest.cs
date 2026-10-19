namespace Wheelhouse.Application.Deployments.Fleets.Models;

/// <summary>Represents one product a fleet rollout promotes: a release ready for one prod target.</summary>
/// <param name="Target">The prod target's slug.</param>
/// <param name="Release">The catalog bundle to promote, as Ready to promote offers it.</param>
/// <param name="Confirm">The target ID, typed; prod on the local server needs it.</param>
public sealed record FleetRolloutItemRequest(string Target, string Release, string? Confirm = null);
