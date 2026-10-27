using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Reads every server's host vitals and every target's container vitals through the runner, one SSH read per
/// host. A read is shared: while it is younger than the holding time every caller — each open tab, an integration —
/// gets it, so the snapshot costs one runner process per holding time however many read it.</summary>
public interface IServerVitals
{
    /// <summary>Reads the snapshot.</summary>
    /// <param name="fresh">Reads the hosts now and replaces the shared snapshot: the owner's explicit Refresh, or a
    /// change the owner just made; a read already under way serves it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The runner's snapshot, or its failure, which is never held.</returns>
    Task<AppResult<JsonElement>> ReadAsync(bool fresh, CancellationToken ct);
}
