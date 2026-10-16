using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Servers.Commands;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Application.Servers.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes the hosts Wheelhouse deploys to over HTTP.</summary>
[ApiController]
[Route("api/servers")]
public sealed class ServersController(ISender sender) : ControllerBase
{
    private const string Slug = InventoryPatternConstants.Slug;

    /// <summary>Gets every server without probing it.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ServerDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ServerGetAllQuery(), ct));

    /// <summary>Adds a server; it reaches nothing until its SSH identity and pinned host key are on the control host.</summary>
    [HttpPost]
    [RequireActionHeader("server")]
    [ProducesResponseType<ApiResponse<ServerDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateServerApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ServerCreateCommand
        {
            Slug = request.Slug,
            Name = request.Name,
            Provider = request.Provider,
            Host = request.Host,
            Region = request.Region,
            SshUser = request.SshUser,
            SshPort = request.SshPort,
            Ingress = request.Ingress,
        }, ct), envelope => StatusCode(StatusCodes.Status201Created, envelope));

    /// <summary>Changes a server's definition; its slug stays.</summary>
    [HttpPut("{slug}")]
    [RequireActionHeader("server")]
    [ProducesResponseType<ApiResponse<ServerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([RegularExpression(Slug)] string slug, UpdateServerApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ServerUpdateCommand
        {
            Slug = slug,
            Name = request.Name,
            Provider = request.Provider,
            Host = request.Host,
            Region = request.Region,
            SshUser = request.SshUser,
            SshPort = request.SshPort,
            Ingress = request.Ingress,
        }, ct));

    /// <summary>Removes a server no target or vault runs on any more.</summary>
    [HttpDelete("{slug}")]
    [RequireActionHeader("server")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ServerDeleteCommand { Slug = slug }, ct), _ => NoContent());
}
