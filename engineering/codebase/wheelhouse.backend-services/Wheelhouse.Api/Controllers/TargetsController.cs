using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Targets.Commands;
using Wheelhouse.Application.Targets.Models;
using Wheelhouse.Application.Targets.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes each product's environments on their servers over HTTP.</summary>
[ApiController]
[Route("api/targets")]
public sealed class TargetsController(ISender sender) : ControllerBase
{
    private const string Slug = InventoryPatternConstants.Slug;

    /// <summary>Gets every target, or one product's.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TargetDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery, RegularExpression(Slug)] string? product, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new TargetGetAllQuery { Product = product }, ct));

    /// <summary>Adds an environment of a product on a server.</summary>
    [HttpPost]
    [RequireActionHeader("target")]
    [ProducesResponseType<ApiResponse<TargetDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateTargetApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new TargetCreateCommand
        {
            Slug = request.Slug,
            Product = request.Product,
            Server = request.Server,
            Environment = request.Environment,
            Network = request.Network,
            Root = request.Root,
            Settings = request.Settings,
            SmokeChecks = request.SmokeChecks,
            Sites = request.Sites,
        }, ct), envelope => StatusCode(StatusCodes.Status201Created, envelope));

    /// <summary>Changes a target's definition; its slug stays.</summary>
    [HttpPut("{slug}")]
    [RequireActionHeader("target")]
    [ProducesResponseType<ApiResponse<TargetDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([RegularExpression(Slug)] string slug, UpdateTargetApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new TargetUpdateCommand
        {
            Slug = slug,
            Product = request.Product,
            Server = request.Server,
            Environment = request.Environment,
            Network = request.Network,
            Root = request.Root,
            Settings = request.Settings,
            SmokeChecks = request.SmokeChecks,
            Sites = request.Sites,
        }, ct));

    /// <summary>Removes a target from the inventory; what runs on its host stays until torn down there.</summary>
    [HttpDelete("{slug}")]
    [RequireActionHeader("target")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new TargetDeleteCommand { Slug = slug }, ct), _ => NoContent());
}
