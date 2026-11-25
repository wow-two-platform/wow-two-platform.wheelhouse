using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Application.SystemStatus.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;
using WoW.Two.Sdk.Beta.Be.Web.SystemStatus;

namespace Wheelhouse.Api.Controllers;

/// <summary>Reports system status. Readiness for deployment health gates, <c>/api/system/ready</c>, is the SDK readiness
/// endpoint over the database check (<c>AddPlatformDefaults</c>).</summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController(ISender sender) : ControllerBase
{
    /// <summary>The running build's informational version: the product version plus the commit it was built from.</summary>
    private static readonly string Version = BuildVersion.Read(typeof(SystemController).Assembly).Version;

    /// <summary>Reports service liveness, version, deployment environment and rehearsal mode.</summary>
    [AllowAnonymous]
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var result = await sender.SendAsync(new GetSystemStatusQuery(Version), cancellationToken);

        return result.Match<IActionResult>(success => Ok(success.Data), failure => this.ToProblem(failure.Error));
    }
}
