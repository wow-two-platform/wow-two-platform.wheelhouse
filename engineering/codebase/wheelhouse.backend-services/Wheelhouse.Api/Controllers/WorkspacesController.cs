using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Workspaces.Commands;
using Wheelhouse.Application.Workspaces.Models;
using Wheelhouse.Application.Workspaces.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes the workspaces products are grouped into over HTTP. Writes carry the <c>workspace</c> action and land
/// in the audit trail.</summary>
[ApiController]
[Route("api/workspaces")]
public sealed class WorkspacesController(ISender sender) : ControllerBase
{
    /// <summary>Gets every workspace, in the owner's order. A key granting <c>catalog:read</c> may read them, since
    /// every product names its workspace.</summary>
    [HttpGet]
    [Authorize(Policy = AuthConfigurationExtensions.ProductsReadPolicy)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<WorkspaceDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new WorkspaceGetAllQuery(), ct));

    /// <summary>Adds a workspace; it comes last in the order.</summary>
    [HttpPost]
    [RequireActionHeader("workspace")]
    [ProducesResponseType<ApiResponse<WorkspaceDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateWorkspaceApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new WorkspaceCreateCommand
        {
            Slug = request.Slug,
            Name = request.Name,
            Description = request.Description,
        }, ct), envelope => Created("api/workspaces/" + envelope.Data.Id, envelope));

    /// <summary>Puts the workspaces in a new order; the list names every workspace exactly once.</summary>
    [HttpPut("order")]
    [RequireActionHeader("workspace")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<WorkspaceDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reorder(ReorderApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new WorkspaceReorderCommand { Ids = request.Ids }, ct));

    /// <summary>Renames or redescribes a workspace; its slug stays.</summary>
    [HttpPut("{id:guid}")]
    [RequireActionHeader("workspace")]
    [ProducesResponseType<ApiResponse<WorkspaceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateWorkspaceApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new WorkspaceUpdateCommand
        {
            Id = id,
            Name = request.Name,
            Description = request.Description,
        }, ct));

    /// <summary>Removes a workspace that holds no product; refused while one remains.</summary>
    [HttpDelete("{id:guid}")]
    [RequireActionHeader("workspace")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new WorkspaceDeleteCommand { Id = id }, ct), _ => NoContent());
}
