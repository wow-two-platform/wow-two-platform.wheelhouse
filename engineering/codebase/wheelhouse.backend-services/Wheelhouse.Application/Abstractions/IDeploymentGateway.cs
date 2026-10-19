using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Exposes the runner through the control plane.</summary>
public interface IDeploymentGateway
{
    /// <summary>Reads targets, published sites, the local server's fixtures, releases and builds, deployment history and statistics, one outcome, one target's state, a product's branches, or host and container vitals.</summary>
    Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct);
    /// <summary>Checks a target's readiness without changing it, optionally against one release.</summary>
    Task<AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct);
    /// <summary>Submits a trusted release to a configured target; <paramref name="confirm"/> is the typed target ID where the target asks for one, <paramref name="skipDevPass"/> lets prod take a release that has not succeeded on dev, and <paramref name="reason"/> is the owner's one-line why, stored on the job.</summary>
    Task<AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, CancellationToken ct);
    /// <summary>Submits a trusted release as part of the fleet rollout <paramref name="fleet"/>, which the runner keeps on
    /// the job; otherwise as <see cref="StartAsync(string, string, string, string?, bool, string?, CancellationToken)"/>. A
    /// gateway that keeps no fleet submits it as an ordinary deploy.</summary>
    Task<AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, Guid? fleet, CancellationToken ct) =>
        StartAsync(target, release, actor, confirm, skipDevPass, reason, ct);
    /// <summary>Submits a trusted release with this deploy's settings layer: <paramref name="settings"/> is
    /// <c>{"values": {key: value}}</c>, the target environment's stored settings and vault secrets, which travel to the
    /// target over SSH stdin and are rendered there, never stored. A gateway without rendering submits it as an ordinary
    /// deploy.</summary>
    Task<AppResult<JsonElement>> StartAsync(string target, string release, string actor, string? confirm, bool skipDevPass,
        string? reason, Guid? fleet, JsonElement? settings, CancellationToken ct) =>
        StartAsync(target, release, actor, confirm, skipDevPass, reason, fleet, ct);
    /// <summary>Checks a target's readiness with the setting keys Wheelhouse would provide (<paramref name="keys"/>, no
    /// values), so the check names a required key no layer sets.</summary>
    Task<AppResult<JsonElement>> CheckAsync(string target, string? release, IReadOnlyCollection<string>? keys, CancellationToken ct) =>
        CheckAsync(target, release, ct);
    /// <summary>Reads every setting a release's services take on a target: required and optional keys, which read as
    /// secret, and what the render derives. No target is contacted. A gateway that renders nothing declares none.</summary>
    Task<AppResult<JsonElement>> SettingsAsync(string target, string release, CancellationToken ct) =>
        Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(new { services = Array.Empty<object>() })));
    /// <summary>Records the owner's reconciliation of an interrupted or failed rollout on its target.</summary>
    Task<AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct);
    /// <summary>Reads a branch's recent commits of a product, each with its build when one exists.</summary>
    Task<AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct);
    /// <summary>Reads the release bodies and compare commits between what a target runs and the release to promote.</summary>
    Task<AppResult<JsonElement>> ChangelogAsync(string product, string? from, string to, CancellationToken ct);
    /// <summary>Starts the product's build workflow for a commit that has no build yet.</summary>
    Task<AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct);
    /// <summary>Reads the last <paramref name="tail"/> lines one service's container wrote on a target.</summary>
    Task<AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct);
}
