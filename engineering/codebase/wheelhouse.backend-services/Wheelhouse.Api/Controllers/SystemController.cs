using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wheelhouse.Api.Configurations;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Web.SystemStatus;

namespace Wheelhouse.Api.Controllers;

/// <summary>Reports system status.</summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController(
    HealthCheckService health, DeploymentSettings deployment, IHostEnvironment host, IConfiguration configuration)
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

    /// <summary>The environment the runner named, when it is <c>dev</c> or <c>prod</c>; else the host's own rule.</summary>
    private string ServedEnvironment() =>
        configuration[DeployEnvironmentKey]?.Trim().ToLowerInvariant() switch
        {
            "dev" => "dev",
            "prod" => "prod",
            _ => host.IsProduction() ? "prod" : "dev",
        };
}
