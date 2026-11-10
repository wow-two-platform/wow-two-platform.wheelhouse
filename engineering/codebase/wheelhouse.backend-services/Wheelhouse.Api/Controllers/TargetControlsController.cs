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

/// <summary>Stops, starts and restarts what an environment runs (decision 45), resets its database (v0.9) or one
/// migration set of it, and runs a one-off command in a service's release image (v0.13). Signed-in owner only: an
/// integration key is refused, and no MCP tool reaches it.</summary>
[ApiController]
[Route("api/deployments/targets/{target}")]
[Authorize(Policy = AuthConfigurationExtensions.OwnerSessionPolicy)]
public sealed class TargetControlsController(ISender sender, IActorContext actor) : ControllerBase
{
    private const string Slug = FormatPatternConstants.NameSlug;

    /// <summary>Queues a stop, start or restart of the release the target runs — one service, or the whole environment
    /// without one — a reset of its database or of one migration set (<c>set</c>), or a run of <c>command</c> in one
    /// service's release image, as a job in its journal; its outcome reads like a deploy's, a run's with its exit code and
    /// last output line. Prod needs its target ID typed and a reason. Refused (409) for Wheelhouse's own
    /// environments, while a rollout, a fleet or another control runs there, and when the target's state forbids it (a
    /// set the running release does not carry, or whose service has no migrate item).</summary>
    [HttpPost("controls")]
    [RequireActionHeader("control")]
    public async Task<IActionResult> Control([RegularExpression(Slug)] string target, TargetControlRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(new TargetControlCommand(
            target, request.Operation!.Value, request.Service, actor.Actor, request.Reason, request.Confirm, request.Set,
            request.Command, request.Input), ct);
        return this.ToActionResult(result, envelope => Accepted(envelope));
    }

    /// <summary>Reads a run's last line as its command printed it, once (v0.13) — an invitation link the job keeps cut —
    /// as <c>{ line }</c>, never cached. The target removes it with this read; a second read, one past 15 minutes or a
    /// job that is no run of the target is refused (409). The audit names the run, never the line.</summary>
    [HttpPost("controls/{job:guid}/reveal")]
    [RequireActionHeader("control")]
    public async Task<IActionResult> Reveal([RegularExpression(Slug)] string target, Guid job, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await sender.SendAsync(new RunRevealCommand(target, job, actor.Actor), ct);
        return this.ToActionResult(result);
    }
}
