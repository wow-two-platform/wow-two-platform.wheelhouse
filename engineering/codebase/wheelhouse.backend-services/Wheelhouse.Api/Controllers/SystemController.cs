using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Web.SystemStatus;

namespace Wheelhouse.Api.Controllers;

/// <summary>Reports system status. Readiness for deployment health gates, <c>/api/system/ready</c>, is the SDK readiness
/// endpoint over the database check (<c>AddPlatformDefaults</c>).</summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController(DeploymentSettings deployment, IConsoleIdentity console) : ControllerBase
{
    /// <summary>The running build's informational version: the product version plus the commit it was built from.</summary>
    private static readonly string Version = BuildVersion.Read(typeof(SystemController).Assembly).Version;

    /// <summary>Reports service liveness and version, the environment this console serves (<see cref="IConsoleIdentity.Environment"/>:
    /// the runner's <c>DEPLOY_ENVIRONMENT</c>, else <c>prod</c> when it runs as Production and <c>dev</c> otherwise; its own
    /// CI deploys it, so the Products page shows it there) and whether this instance drives the local rehearsal rig instead
    /// of real hosts.</summary>
    [AllowAnonymous]
    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(new
        {
            service = "Wheelhouse", status = "ok", version = Version, environment = console.Environment,
            localRig = deployment.IsLocalRig,
        });
    }
}
