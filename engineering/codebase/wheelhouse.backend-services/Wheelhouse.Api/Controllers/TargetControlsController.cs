using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Controls.Commands;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Api.Controllers;

/// <summary>Stops, starts and restarts what an environment runs (decision 45), and resets its database (v0.9). Signed-in
/// owner only: an integration key is refused, and no MCP tool reaches it.</summary>
[ApiController]
[Route("api/deployments/targets/{target}")]
[Authorize(Policy = AuthConfigurationExtensions.OwnerSessionPolicy)]
public sealed class TargetControlsController(ISender sender, IActorContext actor) : ControllerBase
{
    private const string Slug = FormatPatternConstants.NameSlug;

    /// <summary>Queues a stop, start or restart of the release the target runs — one service, or the whole environment
    /// without one — or a reset of its database, as a job in its journal; its outcome reads like a deploy's. Prod needs
    /// its target ID typed and a reason. Refused (409) for Wheelhouse's own environments, while a rollout, a fleet or
    /// another control runs there, and when the target's state forbids it.</summary>
    [HttpPost("controls")]
    [RequireActionHeader("control")]
    public async Task<IActionResult> Control([RegularExpression(Slug)] string target, TargetControlRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(new TargetControlCommand(
            target, request.Operation!.Value, request.Service, actor.Actor, request.Reason, request.Confirm), ct);
        return this.ToActionResult(result, envelope => Accepted(envelope));
    }
}
