using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Integrations.Processes;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Infrastructure.Infra;

/// <summary>Runs the runner's <c>infra.py</c> beside <c>transport.py</c> through the runner's process client
/// (<see cref="RunnerProcess"/>): an argument list, never a shell, no secret in any argument, and the environment allowlist. The OAuth client file and the SSH keys stay on disk under the runner root. A long job is
/// recorded first, then run as this process's own child (<c>enroll-run</c>, <c>place-run</c>, <c>upkeep-run</c>), which
/// nothing waits for:
/// every call here returns within its timeout, and the console reaps the job when it ends.</summary>
/// <remarks>Only configuration exposes the local rig, and only the rig may point the runner at another Tailscale API
/// (<c>Infra:TailscaleApi</c>, its fake); <c>Infra:KitPath</c> names the products kit when it is not at <c>/app/kit</c>.
/// </remarks>
public sealed class InfraGateway(
    [FromKeyedServices(RunnerProcess.ClientName)] IProcessClient runner,
    DeploymentSettings settings,
    IConfiguration configuration) : IInfraGateway
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
        // What the product's deploy.yml declares, so the runner refuses what it does not call for.
        foreach (var site in request.Descriptor.Sites)
            arguments.AddRange(["--declared-site", site.Name + "=" + site.Exposure]);
        if (request.Descriptor.NeedsPostgres)
            arguments.Add("--needs-postgres");
        return StartAsync([.. arguments, "--no-launch"],
            job => job.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var parsed)
                ? ["place-run", "--job", parsed.ToString("D")]
                : null, ct);
    }

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> UpkeepsAsync(string server, CancellationToken ct) =>
        RunAsync(["upkeeps", "--server", server], ct);

    /// <inheritdoc />
    public Task<AppResult<JsonElement>> UpkeepAsync(string server, string actor, CancellationToken ct) =>
        StartAsync(["upkeep", "--server", server, "--actor", actor, "--no-launch"],
            job => job.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var parsed)
                ? ["upkeep-run", "--job", parsed.ToString("D")]
                : null, ct);

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
        return runner.Start(Spec(follow)).Match(
            _ => recorded,
            _ => AppResult<JsonElement>.Fail(AppErrorFactory.ExternalUnavailable(RunnerFailureWording.Infra.Unavailable)));
    }

    /// <summary>Describes one call: only configuration exposes the local rig, the fake Tailscale API and another kit.</summary>
    private ProcessSpec Spec(string[] arguments)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
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
        return RunnerProcess.Spec(settings, ScriptPath, arguments, environment);
    }

    private async Task<AppResult<JsonElement>> RunAsync(string[] arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(ScriptPath))
            return AppResult<JsonElement>.Fail(AppErrorFactory.Unexpected("The infrastructure runner is not installed."));

        var result = await runner.RunJsonAsync(Spec(arguments), ct);
        return result.Match(
            data => AppResult<JsonElement>.Ok(data),
            error => AppResult<JsonElement>.Fail(RunnerFailureWording.Infra.Describe(error)));
    }
}
