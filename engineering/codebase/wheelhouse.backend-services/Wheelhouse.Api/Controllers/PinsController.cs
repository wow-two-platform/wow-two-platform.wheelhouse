using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Pins.Commands;
using Wheelhouse.Application.Pins.Models;
using Wheelhouse.Application.Pins.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes the signed-in operator's pins over HTTP. Pins are a preference: operators' sessions only (an
/// integration key gets 403), writes carry the <c>pin</c> action, and nothing reaches the audit trail.</summary>
[ApiController]
[Route("api/pins")]
[Authorize(Policy = AuthConfigurationExtensions.OperatorSessionPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PinsController(ISender sender) : ControllerBase
{
    /// <summary>Gets the operator's pins, in the operator's order.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PinDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new PinGetAllQuery(), ct));

    /// <summary>Pins a workspace, product or service; it comes last. Pinning a target again answers its existing pin
    /// with 200 instead of 201.</summary>
    [HttpPost]
    [RequireActionHeader("pin")]
    [ProducesResponseType<ApiResponse<PinDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<PinDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreatePinApiRequest request, CancellationToken ct) =>
        (await sender.SendAsync(new PinCreateCommand
        {
            Kind = request.Kind,
            WorkspaceId = request.WorkspaceId,
            Product = request.ProductId,
            Service = request.Service,
        }, ct)).Match<IActionResult>(
            ok => ok.Data.Created
                ? Created("api/pins/" + ok.Data.Pin.Id, ApiResponse<PinDto>.Ok(ok.Data.Pin))
                : Ok(ApiResponse<PinDto>.Ok(ok.Data.Pin)),
            fail => this.ToProblem(fail.Error));

    /// <summary>Puts the operator's pins in a new order; the list names every pin exactly once.</summary>
    [HttpPut("order")]
    [RequireActionHeader("pin")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PinDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reorder(ReorderApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new PinReorderCommand { Ids = request.Ids }, ct));

    /// <summary>Removes one of the operator's pins.</summary>
    [HttpDelete("{id:guid}")]
    [RequireActionHeader("pin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new PinDeleteCommand { Id = id }, ct), _ => NoContent());
}
