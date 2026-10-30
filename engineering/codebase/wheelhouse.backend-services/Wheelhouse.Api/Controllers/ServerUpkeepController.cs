using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Commands;
using Wheelhouse.Application.Servers.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes an enrolled server's upkeep: a recorded job that brings the host to the kit this console ships, its
/// firewall and platform included, with live steps.</summary>
[ApiController]
[Route("api/servers/{slug}/upkeep")]
public sealed class ServerUpkeepController(ISender sender, IActorContext actor) : ControllerBase
{
    private const string Slug = FormatPatternConstants.NameSlug;

    /// <summary>Gets the server's upkeep runs, newest first, and the digest of the kit this console ships.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ServerUpkeepsQuery(slug), ct));

    /// <summary>Starts an upkeep run on the server.</summary>
    [HttpPost]
    [RequireActionHeader("server")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ServerUpkeepCommand { Slug = slug, Actor = actor.Actor }, ct));
}
