using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Infrastructure.Deployments.Parsers;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Infra;

/// <summary>Runs the runner's <c>infra.py</c> beside <c>transport.py</c>: an argument list, never a shell, and no
/// secret in any argument. The OAuth client file and the SSH keys stay on disk under the runner root. A long job is
/// recorded first, then run as this process's own child (<c>enroll-run</c>, <c>place-run</c>), which nothing waits for:
/// every call here returns within its timeout, and the console reaps the job when it ends.</summary>
/// <remarks>Only configuration exposes the local rig, and only the rig may point the runner at another Tailscale API
/// (<c>Infra:TailscaleApi</c>, its fake); <c>Infra:KitPath</c> names the products kit when it is not at <c>/app/kit</c>.
/// </remarks>
public sealed class InfraGateway(DeploymentSettings settings, IConfiguration configuration, RunnerFailureParser failures)
    : IInfraGateway
{
    /// <summary>The runner script, beside the transport.</summary>
    public const string Script = "infra.py";

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> TailnetAsync(CancellationToken ct) => RunAsync(["tailnet"], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> PolicyAsync(CancellationToken ct) => RunAsync(["policy"], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> ApplyPolicyAsync(string etag, string digest, string actor, CancellationToken ct) =>
        RunAsync(["policy-apply", "--etag", etag, "--digest", digest, "--actor", actor], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> RevertPolicyAsync(string etag, string actor, CancellationToken ct) =>
        RunAsync(["policy-revert", "--etag", etag, "--actor", actor], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> EnrollmentAsync(string server, CancellationToken ct) =>
        RunAsync(["enrollment", "--server", server], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> EnrollmentKeyAsync(string server, CancellationToken ct) =>
        RunAsync(["enrollment-key", "--server", server], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> EnrollAsync(ServerEnrollmentRequest request, string actor, CancellationToken ct)
    {
        List<string> arguments =
        [
            "enroll", "--server", request.Server, "--address", request.PublicAddress,
            "--port", request.SshPort.ToString(CultureInfo.InvariantCulture), "--login-user", request.LoginUser,
            "--admin", request.Admin, "--tailnet-hostname", request.TailnetHostname, "--actor", actor,
        ];
        if (request.AdminKey is not null)
            arguments.AddRange(["--admin-key", request.AdminKey]);
        if (request.ExpectedHostKey is not null)
            arguments.AddRange(["--expected-host-key", request.ExpectedHostKey]);
        return StartAsync([.. arguments, "--no-launch"], _ => ["enroll-run", "--server", request.Server], ct);
    }

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> PlacementsAsync(string product, CancellationToken ct) =>
        RunAsync(["placements", "--product", product], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> PlaceAsync(ProductPlacementRequest request, string actor, CancellationToken ct)
    {
        List<string> arguments = ["place", "--server", request.Server, "--product", request.Product, "--actor", actor];
        if (request.Database)
            arguments.Add("--database");
        foreach (var site in request.Sites)
            arguments.AddRange(["--site", site.Site + "=" + site.Node]);
        return StartAsync([.. arguments, "--no-launch"],
            job => job.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var parsed)
                ? ["place-run", "--job", parsed.ToString("D")]
                : null, ct);
    }

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> FinishedAsync(CancellationToken ct) => RunAsync(["finished"], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> AcknowledgeAsync(string job, CancellationToken ct) =>
        RunAsync(["acknowledge", "--job", job], ct);

    /// <summary>The script's path: <c>infra.py</c> in the transport's folder.</summary>
    public string ScriptPath => Path.Combine(Path.GetDirectoryName(settings.TransportPath) ?? ".", Script);

    /// <summary>Records a job, then starts its run as a child no one waits for; its output is read and dropped.</summary>
    private async Task<AppResult<JsonElement>> StartAsync(
        string[] arguments, Func<JsonElement, string[]?> run, CancellationToken ct)
    {
        var recorded = await RunAsync(arguments, ct);
        if (recorded is not AppResult<JsonElement>.Success { Data: var job } || run(job) is not { } follow)
            return recorded;
        var process = Create(follow);
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += static (_, _) => { };
        process.ErrorDataReceived += static (_, _) => { };
        process.Exited += static (sender, _) => ((Process)sender!).Dispose();
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            process.Dispose();
            return AppResult<JsonElement>.Fail(AppErrorFactory.ExternalUnavailable("The infrastructure runner is unavailable."));
        }

        return recorded;
    }

    private Process Create(string[] arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(settings.Python)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        var environment = process.StartInfo.Environment;
        foreach (var name in new[] { "WHEELHOUSE_REHEARSAL", "REHEARSAL_STATE", "WHEELHOUSE_TAILSCALE_API", "WHEELHOUSE_KIT" })
            environment.Remove(name);
        if (settings.IsLocalRig)
        {
            environment["WHEELHOUSE_REHEARSAL"] = settings.Rehearsal == DeploymentSettings.RigNetwork ? "network" : "1";
            if (configuration["Infra:TailscaleApi"] is { Length: > 0 } api)
                environment["WHEELHOUSE_TAILSCALE_API"] = api;
        }
        if (settings.RehearsalState.Length > 0)
            environment["REHEARSAL_STATE"] = settings.RehearsalState;
        if (configuration["Infra:KitPath"] is { Length: > 0 } kit)
            environment["WHEELHOUSE_KIT"] = kit;
        process.StartInfo.ArgumentList.Add(ScriptPath);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.ArgumentList.Add("--root");
        process.StartInfo.ArgumentList.Add(settings.Root);
        return process;
    }

    private async Task<AppResult<JsonElement>> RunAsync(string[] arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(ScriptPath))
            return AppResult<JsonElement>.Fail(AppErrorFactory.Unexpected("The infrastructure runner is not installed."));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(110));
        using var process = Create(arguments);
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var standardError = await error;
            if (process.ExitCode != 0)
                return AppResult<JsonElement>.Fail(Refusal(standardError)
                    ?? AppErrorFactory.Unexpected("The infrastructure action failed. Inspect the control host privately."));
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
            return AppResult<JsonElement>.Fail(AppErrorFactory.OperationTimeout("The infrastructure action timed out."));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or JsonException or IOException)
        {
            return AppResult<JsonElement>.Fail(AppErrorFactory.ExternalUnavailable("The infrastructure runner is unavailable."));
        }
    }

    /// <summary>The runner's own display-safe reason, without the deployment wording the shared parser adds.</summary>
    private AppError? Refusal(string standardError) =>
        failures.Parse(standardError) switch
        {
            null => null,
            var parsed when parsed.Message.StartsWith("Deployment rejected: ", StringComparison.Ordinal) =>
                AppErrorFactory.Conflict(parsed.Message["Deployment rejected: ".Length..]),
            var parsed when parsed.Message.StartsWith("Deployment step failed: ", StringComparison.Ordinal) =>
                AppErrorFactory.ExternalUnavailable(parsed.Message["Deployment step failed: ".Length..]),
            var parsed => parsed,
        };
}
