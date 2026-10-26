using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Integrations.Processes;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Runs the same bounded SSH adapter used by manual recovery, through the runner's process client
/// (<see cref="RunnerProcess"/>): an argument list, the environment allowlist, and the process tree killed past its time.</summary>
/// <remarks>A runner action that reaches GitHub first gets the GitHub App's per-owner tokens written for it
/// (<see cref="GitHubRunnerTokens"/>); every action may note refused GitHub calls for the Issues panel.</remarks>
public sealed class DeploymentGateway(
    [FromKeyedServices(RunnerProcess.ClientName)] IProcessClient runner,
    DeploymentSettings settings,
    GitHubRunnerTokens? github = null) : IDeploymentGateway
{
    /// <summary>Holds the runner actions that call GitHub: release and build listings, downloads, dispatches.</summary>
    private static readonly HashSet<string> GitHubActions = new(StringComparer.Ordinal)
    {
        "releases", "latest", "builds", "usage", "branches", "commits", "changelog", "build", "submit", "facts", "check",
        "settings",
    };

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct) =>
        resource switch
        {
            "sites" or "fixtures" or "targets" or "releases" or "jobs" or "follow" or "history" or "latest" or "builds" or "retention" => RunAsync([resource], ct),
            "status" when Guid.TryParse(id, out _) => RunAsync(["status", "--job", id], ct),
            "observation" when Guid.TryParse(id, out _) => RunAsync(["observation", "--job", id], ct),
            "state" when id is not null => RunAsync(["state", "--target", id], ct),
            "topology" when id is not null => RunAsync(["topology", "--target", id], ct),
            "branches" when id is not null => RunAsync(["branches", "--product", id], ct),
            "facts" when id is not null => RunAsync(["facts", "--bundle", id], ct),
            "usage" when id is not null => RunAsync(["usage", "--product", id], ct),
            "watch" when int.TryParse(id, out var minutes) => RunAsync(["watch", "--minutes", minutes.ToString(CultureInfo.InvariantCulture)], ct),
            "vitals" => RunAsync(id is null ? ["vitals"] : ["vitals", "--target", id], ct),
            "stats" when int.TryParse(id, out var days) => RunAsync(["stats", "--days", days.ToString(CultureInfo.InvariantCulture)], ct),
            _ => Task.FromResult(AppResult<JsonElement>.Fail(AppErrorFactory.NotFound("Unknown deployment resource.")))
        };

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct) =>
        RunAsync(release is null ? ["check", "--target", target] : ["check", "--target", target, "--bundle", release], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> CheckAsync(string target, string? release, IReadOnlyCollection<string>? keys, CancellationToken ct)
    {
        if (keys is null)
            return CheckAsync(target, release, ct);
        List<string> arguments = release is null ? ["check", "--target", target] : ["check", "--target", target, "--bundle", release];
        arguments.Add("--stdin");
        return RunAsync([.. arguments], ct, JsonSerializer.Serialize(new { keys }));
    }

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> SettingsAsync(string target, string release, CancellationToken ct) =>
        RunAsync(["settings", "--target", target, "--bundle", release], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> StartAsync(
        string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, CancellationToken ct) =>
        StartAsync(target, release, actor, confirm, skipDevPass, reason, null, ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> StartAsync(
        string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, Guid? fleet,
        CancellationToken ct) =>
        StartAsync(target, release, actor, confirm, skipDevPass, reason, fleet, null, ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> StartAsync(
        string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, Guid? fleet,
        JsonElement? settings, CancellationToken ct)
    {
        List<string> arguments = ["submit", "--target", target, "--bundle", release, "--actor", actor];
        if (fleet is { } id)
            arguments.AddRange(["--fleet", id.ToString("D")]);
        if (confirm is not null)
            arguments.AddRange(["--confirm", confirm]);
        if (skipDevPass)
            arguments.Add("--skip-dev-pass");
        // An argument list, never a shell: the reason reaches the runner as one value.
        if (reason is not null)
            arguments.AddRange(["--reason", reason]);
        // The settings values go to the runner's stdin, never an argument a process listing would show.
        if (settings is not null)
            arguments.Add("--stdin");
        return RunAsync([.. arguments], ct, settings?.GetRawText());
    }

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct) =>
        RunAsync(["reconcile", "--target", target, "--job", job, "--actor", actor], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> ControlAsync(
        string target, string operation, string? service, string actor, string? reason, CancellationToken ct)
    {
        List<string> arguments = ["control", "--target", target, "--operation", operation, "--actor", actor];
        if (service is not null)
            arguments.AddRange(["--service", service]);
        // An argument list, never a shell: the reason reaches the runner as one value.
        if (reason is not null)
            arguments.AddRange(["--reason", reason]);
        return RunAsync([.. arguments], ct);
    }

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct) =>
        RunAsync(["commits", "--product", product, "--branch", branch], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> ChangelogAsync(string product, string? from, string to, CancellationToken ct) =>
        RunAsync(from is null ? ["changelog", "--product", product, "--to", to] : ["changelog", "--product", product, "--from", from, "--to", to], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct) =>
        RunAsync(["build", "--product", product, "--commit", commit], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> BranchesAsync(string product, string? repository, CancellationToken ct) =>
        RunAsync(repository is null ? ["branches", "--product", product] : ["branches", "--product", product, "--repository", repository], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> CommitsAsync(string product, string branch, string? repository, CancellationToken ct) =>
        RunAsync(repository is null
            ? ["commits", "--product", product, "--branch", branch]
            : ["commits", "--product", product, "--branch", branch, "--repository", repository], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> RequestBuildAsync(string product, string commit, string? repository, CancellationToken ct) =>
        RunAsync(repository is null
            ? ["build", "--product", product, "--commit", commit]
            : ["build", "--product", product, "--commit", commit, "--repository", repository], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct) =>
        RunAsync(["logs", "--target", target, "--service", service, "--tail", tail.ToString(CultureInfo.InvariantCulture)], ct);

    /// <summary>Writes the per-owner tokens for an action that reaches GitHub; without them the runner falls back.</summary>
    private async Task<string?> GitHubTokensAsync(string action, CancellationToken ct)
    {
        if (github is null || !GitHubActions.Contains(action))
            return null;
        try
        {
            return await github.PrepareAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null; // The inventory could not be read: the token file or anonymous access still serves.
        }
    }

    private async Task<AppResult<JsonElement>> RunAsync(string[] arguments, CancellationToken ct, string? input = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(settings.TransportPath))
            return AppResult<JsonElement>.Fail(AppErrorFactory.Unexpected("Deployment runner is not installed."));

        // Only the API names the token files, the access notes and the local rig: nothing ambient passes the allowlist.
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["WHEELHOUSE_GITHUB_TOKEN_FILE"] = settings.TokenFile(),
        };
        if (await GitHubTokensAsync(arguments[0], ct) is { } tokens)
            environment["WHEELHOUSE_GITHUB_TOKENS_FILE"] = tokens;
        if (settings.Root.Length > 0)
            environment["WHEELHOUSE_GITHUB_ACCESS_FILE"] = GitHubRunnerFiles.AccessPath(settings.Root);
        if (settings.IsLocalRig)
            environment["WHEELHOUSE_REHEARSAL"] = settings.Rehearsal == DeploymentSettings.RigNetwork ? "network" : "1";
        if (settings.RehearsalState.Length > 0)
            environment["REHEARSAL_STATE"] = settings.RehearsalState;

        // The settings values reach the runner's standard input, never an argument a process listing would show.
        var result = await runner.RunJsonAsync(RunnerProcess.Spec(settings, settings.TransportPath, arguments, environment, input), ct);
        return result.Match(
            data => AppResult<JsonElement>.Ok(data),
            error => AppResult<JsonElement>.Fail(RunnerFailureWording.Deployment.Describe(error)));
    }
}
