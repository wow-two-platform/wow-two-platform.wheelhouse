using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Operations;

/// <summary>Reads every server's host vitals and every target's container vitals: the shared snapshot while it is fresh,
/// so every tab reads one runner read.</summary>
/// <param name="Fresh">Reads the hosts now: the owner's explicit Refresh, or a change the owner just made.</param>
public sealed record ServerVitalsQuery(bool Fresh = false) : IQuery<AppResult<JsonElement>>;
