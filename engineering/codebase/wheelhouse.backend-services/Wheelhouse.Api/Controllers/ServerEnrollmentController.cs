using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Servers.Commands;
using Wheelhouse.Application.Servers.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes a fresh server's enrollment: a one-time key the owner adds at order time, then a recorded job that
/// brings the host online and onto the tailnet.</summary>
[ApiController]
[Route("api/servers/{slug}/enrollment")]
public sealed class ServerEnrollmentController(ISender sender, IActorContext actor) : ControllerBase
{
    private const string Slug = FormatPatternConstants.NameSlug;

    /// <summary>Gets the server's enrollment with its live steps.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ServerEnrollmentQuery(slug), ct));

    /// <summary>Creates the one-time enrollment key, once, and returns its public half.</summary>
    [HttpPost("key")]
    [RequireActionHeader("server")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Key([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ServerEnrollmentKeyCommand(slug), ct));

    /// <summary>Starts the enrollment, or reruns it from the first step that has not succeeded.</summary>
    [HttpPost]
    [RequireActionHeader("server")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start([RegularExpression(Slug)] string slug, ServerEnrollApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ServerEnrollCommand
        {
            Slug = slug,
            PublicAddress = request.PublicAddress,
            SshPort = request.SshPort == 0 ? 22 : request.SshPort,
            LoginUser = request.LoginUser,
            Admin = request.Admin,
            AdminKey = request.AdminKey,
            TailnetHostname = request.TailnetHostname,
            ExpectedHostKey = request.ExpectedHostKey,
            Actor = actor.Actor,
        }, ct));
}
