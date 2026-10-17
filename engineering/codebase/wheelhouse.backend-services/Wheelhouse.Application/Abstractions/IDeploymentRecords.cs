using Wheelhouse.Application.Deployments.History.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the read of the runner's deployment history projection; local files only, no target is
/// contacted.</summary>
public interface IDeploymentRecords
{
    /// <summary>Reads every submission the control plane recorded, newest first; held briefly so a page's reads share
    /// one runner process.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The records, or the runner's failure.</returns>
    Task<AppResult<IReadOnlyList<DeploymentRecord>>> ListAsync(CancellationToken ct);

    /// <summary>Reads what the target last reported about one submission: steps, sites and warnings.</summary>
    /// <param name="id">The submission's identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The observation, or the runner's failure.</returns>
    Task<AppResult<DeploymentObservation>> ObserveAsync(Guid id, CancellationToken ct);
}
