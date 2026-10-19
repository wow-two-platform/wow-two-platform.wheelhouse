using System.ComponentModel.DataAnnotations;

namespace Wheelhouse.Api.Requests;

/// <summary>Selects one release Ready to promote for one prod target.</summary>
/// <param name="Target">The prod target.</param>
/// <param name="Release">The catalog entry to promote.</param>
/// <param name="Confirm">The target ID typed out; required for prod on the local server.</param>
public sealed record FleetRolloutItemApiRequest(
    [Required, RegularExpression("^[a-z][a-z0-9-]{0,47}$")] string Target,
    [Required, RegularExpression("^[a-z][a-z0-9-]{0,47}$")] string Release,
    [RegularExpression("^[a-z][a-z0-9-]{0,47}$")] string? Confirm = null);
