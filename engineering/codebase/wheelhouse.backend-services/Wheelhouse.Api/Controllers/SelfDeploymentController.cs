using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Application.Deployments.SelfDeploy.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Reads this console's own newest rollout: after a self-promotion, the new console shows how it ended.</summary>
[ApiController]
[Route("api/self-deployment")]
public sealed class SelfDeploymentController(ISender sender) : ControllerBase
{
    /// <summary>This console's environment, its own target, when it started and its own newest rollout, read again
    /// from the host's journal while it is under way.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new SelfDeploymentQuery(), ct));
}
