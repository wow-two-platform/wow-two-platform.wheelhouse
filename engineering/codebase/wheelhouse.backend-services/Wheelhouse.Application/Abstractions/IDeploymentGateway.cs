using System.Text.Json;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Exposes the operator runner through the control plane.</summary>
public interface IDeploymentGateway
{
    /// <summary>Reads targets, published sites, the local server's fixtures, releases and builds, deployment history and statistics, one outcome, one target's state, a product's branches, or host and container vitals.</summary>
    Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct);
    /// <summary>Checks a target's readiness without changing it, optionally against one release.</summary>
    Task<AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct);
    /// <summary>Submits a trusted release to a configured target; <paramref name="confirm"/> is the typed target ID where the target asks for one, and <paramref name="skipTestPass"/> lets prod take a release that has not succeeded on test.</summary>
    Task<AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skipTestPass, CancellationToken ct);
    /// <summary>Records the operator's reconciliation of an interrupted or failed rollout on its target.</summary>
    Task<AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct);
    /// <summary>Reads a branch's recent commits of a product, each with its build when one exists.</summary>
    Task<AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct);
    /// <summary>Starts the product's build workflow for a commit that has no build yet.</summary>
    Task<AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct);
    /// <summary>Reads the last <paramref name="tail"/> lines one service's container wrote on a target.</summary>
    Task<AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct);
}
