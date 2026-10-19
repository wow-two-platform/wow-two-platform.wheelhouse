using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Tailnet.Commands;
using Wheelhouse.Application.Tailnet.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes Tailscale through the owner's OAuth client: each server's node, and the access policy applied after
/// a reviewed diff. The client's secret and every key stay on the control host.</summary>
[ApiController]
[Route("api/tailnet")]
public sealed class TailnetController(ISender sender, IActorContext actor) : ControllerBase
{
    /// <summary>Gets whether the OAuth client is in place and works, and each server's tailnet node.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new TailnetReadQuery(), ct));

    /// <summary>Gets the live policy beside the one Wheelhouse renders from its inventory, their diff and Tailscale's
    /// validation; nothing changes.</summary>
    [HttpGet("policy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Policy(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new TailnetPolicyQuery(), ct));

    /// <summary>Applies the reviewed policy; the policy it replaces is kept for one-click revert.</summary>
    [HttpPost("policy/apply")]
    [RequireActionHeader("tailnet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Apply(TailnetPolicyApplyApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new TailnetPolicyApplyCommand(request.Etag, request.Digest, actor.Actor), ct));

    /// <summary>Puts back the policy that was live before Wheelhouse's last change.</summary>
    [HttpPost("policy/revert")]
    [RequireActionHeader("tailnet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revert(TailnetPolicyRevertApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new TailnetPolicyRevertCommand(request.Etag, actor.Actor), ct));
}
