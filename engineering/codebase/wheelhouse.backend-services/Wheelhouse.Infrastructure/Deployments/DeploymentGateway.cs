using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Deployments.Parsers;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Backend.Beta.Foundation.Errors;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Infrastructure.Deployments;

/// <summary>Runs the same bounded SSH adapter used by operator recovery.</summary>
public sealed class DeploymentGateway(DeploymentSettings settings, RunnerFailureParser failures) : IDeploymentGateway
{
    /// <inheritdoc />
    public Task<AppResult<JsonElement>> ReadAsync(string resource, string? id, CancellationToken ct) =>
        resource switch
        {
            "sites" or "fixtures" or "targets" or "releases" or "jobs" => RunAsync([resource], ct),
            "status" when Guid.TryParse(id, out _) => RunAsync(["status", "--job", id], ct),
            "state" when id is not null => RunAsync(["state", "--target", id], ct),
            "topology" when id is not null => RunAsync(["topology", "--target", id], ct),
            "branches" when id is not null => RunAsync(["branches", "--product", id], ct),
            "vitals" => RunAsync(id is null ? ["vitals"] : ["vitals", "--target", id], ct),
            "stats" when int.TryParse(id, out var days) => RunAsync(["stats", "--days", days.ToString(CultureInfo.InvariantCulture)], ct),
            _ => Task.FromResult(AppResult<JsonElement>.Fail(AppErrorFactory.NotFound("Unknown deployment resource.")))
        };

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> CheckAsync(string target, string? release, CancellationToken ct) =>
        RunAsync(release is null ? ["check", "--target", target] : ["check", "--target", target, "--bundle", release], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> StartAsync(
        string target, string release, string actor, string? confirm, bool skipTestPass, CancellationToken ct)
    {
        List<string> arguments = ["submit", "--target", target, "--bundle", release, "--actor", actor];
        if (confirm is not null)
            arguments.AddRange(["--confirm", confirm]);
        if (skipTestPass)
            arguments.Add("--skip-test-pass");
        return RunAsync([.. arguments], ct);
    }

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> ReconcileAsync(string target, string job, string actor, CancellationToken ct) =>
        RunAsync(["reconcile", "--target", target, "--job", job, "--actor", actor], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> CommitsAsync(string product, string branch, CancellationToken ct) =>
        RunAsync(["commits", "--product", product, "--branch", branch], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> RequestBuildAsync(string product, string commit, CancellationToken ct) =>
        RunAsync(["build", "--product", product, "--commit", commit], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> LogsAsync(string target, string service, int tail, CancellationToken ct) =>
        RunAsync(["logs", "--target", target, "--service", service, "--tail", tail.ToString(CultureInfo.InvariantCulture)], ct);

    private async Task<AppResult<JsonElement>> RunAsync(string[] arguments, CancellationToken ct)
    {
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
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            return AppResult<JsonElement>.Fail(AppErrorFactory.Unexpected("Deployment response timed out. Reconcile target state before retrying."));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or JsonException or IOException)
        {
            return AppResult<JsonElement>.Fail(AppErrorFactory.Unexpected("Deployment runner is unavailable."));
        }
    }
}
