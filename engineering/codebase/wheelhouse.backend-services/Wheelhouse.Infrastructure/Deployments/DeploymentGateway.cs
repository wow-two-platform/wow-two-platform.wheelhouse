using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Deployments.Parsers;
using Wheelhouse.Infrastructure.GitHub;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Runs the same bounded SSH adapter used by manual recovery.</summary>
/// <remarks>A runner action that reaches GitHub first gets the GitHub App's per-owner tokens written for it
/// (<see cref="GitHubRunnerTokens"/>); every action may note refused GitHub calls for the Issues panel.</remarks>
public sealed class DeploymentGateway(
    DeploymentSettings settings, RunnerFailureParser failures, GitHubRunnerTokens? github = null) : IDeploymentGateway
{
    /// <summary>Holds the runner actions that call GitHub: release and build listings, downloads, dispatches.</summary>
    private static readonly HashSet<string> GitHubActions = new(StringComparer.Ordinal)
    {
        "releases", "latest", "builds", "usage", "branches", "commits", "changelog", "build", "submit", "facts", "check",
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
    public Task<AppResult<JsonElement>> StartAsync(
        string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, CancellationToken ct) =>
        StartAsync(target, release, actor, confirm, skipDevPass, reason, null, ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> StartAsync(
        string target, string release, string actor, string? confirm, bool skipDevPass, string? reason, Guid? fleet,
        CancellationToken ct)
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
        return RunAsync([.. arguments], ct);
    }

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct) =>
        RunAsync(["reconcile", "--target", target, "--job", job, "--actor", actor], ct);

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

    private async Task<AppResult<JsonElement>> RunAsync(string[] arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(settings.TransportPath))
            return AppResult<JsonElement>.Fail(AppErrorFactory.Unexpected("Deployment runner is not installed."));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(110));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(settings.Python)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.Environment["WHEELHOUSE_GITHUB_TOKEN_FILE"] = settings.GitHubTokenFile;
        // Only the API names the per-owner tokens and the access notes; ambient values never reach the runner.
        process.StartInfo.Environment.Remove("WHEELHOUSE_GITHUB_TOKENS_FILE");
        process.StartInfo.Environment.Remove("WHEELHOUSE_GITHUB_ACCESS_FILE");
        if (await GitHubTokensAsync(arguments[0], ct) is { } tokens)
            process.StartInfo.Environment["WHEELHOUSE_GITHUB_TOKENS_FILE"] = tokens;
        if (settings.Root.Length > 0)
            process.StartInfo.Environment["WHEELHOUSE_GITHUB_ACCESS_FILE"] = GitHubRunnerFiles.AccessPath(settings.Root);
        // Only configuration exposes the local rig; a stray environment variable cannot.
        process.StartInfo.Environment.Remove("WHEELHOUSE_REHEARSAL");
        process.StartInfo.Environment.Remove("REHEARSAL_STATE");
        if (settings.IsLocalRig)
            process.StartInfo.Environment["WHEELHOUSE_REHEARSAL"] = settings.Rehearsal == DeploymentSettings.RigNetwork ? "network" : "1";
        if (settings.RehearsalState.Length > 0)
            process.StartInfo.Environment["REHEARSAL_STATE"] = settings.RehearsalState;
        process.StartInfo.ArgumentList.Add(settings.TransportPath);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.ArgumentList.Add("--root");
        process.StartInfo.ArgumentList.Add(settings.Root);
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var standardError = await error;
            if (process.ExitCode != 0)
                return AppResult<JsonElement>.Fail(failures.Parse(standardError)
                    ?? AppErrorFactory.Unexpected("Deployment operation failed. Inspect the target privately."));
            using var document = JsonDocument.Parse(await output);
            return AppResult<JsonElement>.Ok(document.RootElement.Clone());
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process may exit between the state check and cancellation.
            }
            ct.ThrowIfCancellationRequested();
            // Timeouts and an unreachable runner are transient kinds, so callers (the MCP tools) can mark them retryable.
            return AppResult<JsonElement>.Fail(AppErrorFactory.OperationTimeout("Deployment response timed out. Reconcile target state before retrying."));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or JsonException or IOException)
        {
            return AppResult<JsonElement>.Fail(AppErrorFactory.ExternalUnavailable("Deployment runner is unavailable."));
        }
    }
}
