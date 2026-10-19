using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Configurations;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.SystemStatus;

namespace Wheelhouse.Api.Controllers;

/// <summary>Reports system status.</summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController(
    HealthCheckService health, DeploymentSettings deployment, ConsoleSettings console, IHostEnvironment host,
    IConfiguration configuration)
    : ControllerBase
{
    /// <summary>The variable the runner gives every deployed service: the environment it runs in.</summary>
    public const string DeployEnvironmentKey = "DEPLOY_ENVIRONMENT";

    /// <summary>Reports database readiness for deployment health gates, from the SDK database readiness check.</summary>
    [AllowAnonymous]
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct) =>
        (await health.CheckHealthAsync(check => check.Tags.Contains(WheelhouseDatabase.ReadinessTag), ct)).Status
            == HealthStatus.Healthy
            ? Ok()
            : StatusCode(503);

    /// <summary>The running build's informational version: the product version plus the commit it was built from.</summary>
    private static readonly string Version = BuildVersion.Read(typeof(SystemController).Assembly).Version;

    /// <summary>Reports service liveness and version, the environment this console serves — <c>DEPLOY_ENVIRONMENT</c>,
    /// which the runner gives every deployed service, else <c>prod</c> when it runs as Production and <c>dev</c> otherwise;
    /// its own CI deploys it, so the Products page shows it there — and whether this instance drives the local rehearsal
    /// rig instead of real hosts.</summary>
    [AllowAnonymous]
    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(new
        {
            service = "Wheelhouse", status = "ok", version = Version, environment = ServedEnvironment(),
            localRig = deployment.IsLocalRig,
        });
    }

    /// <summary>Reports where the other console answers and which environment it serves, for the environment tag's
    /// switch. The address is a host fact: only the owner's session reads it, never the anonymous status or an
    /// integration key, and it is empty (both fields <see langword="null"/>) until <c>Console:PeerUrl</c> is set.</summary>
    [Authorize(Policy = AuthConfigurationExtensions.OwnerSessionPolicy)]
    [HttpGet("peer")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<ApiResponse<PeerConsole>>(StatusCodes.Status200OK)]
    public IActionResult Peer() =>
        Ok(ApiResponse<PeerConsole>.Ok(console.PeerOrigin is { } url
            ? new PeerConsole(url, ServedEnvironment() == "prod" ? "dev" : "prod")
            : new PeerConsole(null, null)));

    /// <summary>The other console, as the environment tag reads it.</summary>
    /// <param name="Url">The other console's origin, <c>https://host[:port]</c>; <see langword="null"/> without a peer.</param>
    /// <param name="Environment">The environment the other console serves, <c>dev</c> or <c>prod</c>.</param>
    public sealed record PeerConsole(string? Url, string? Environment);

    /// <summary>The environment the runner named, when it is <c>dev</c> or <c>prod</c>; else the host's own rule.</summary>
    private string ServedEnvironment() =>
        configuration[DeployEnvironmentKey]?.Trim().ToLowerInvariant() switch
        {
            "dev" => "dev",
            "prod" => "prod",
            _ => host.IsProduction() ? "prod" : "dev",
        };
}
