using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes a product's placement on servers: its dev and prod databases and its private sites, each as a
/// recorded job.</summary>
[ApiController]
[Route("api/products/{slug}/placements")]
public sealed class ProductPlacementsController(ISender sender, IActorContext actor) : ControllerBase
{
    private const string Slug = FormatPatternConstants.NameSlug;

    /// <summary>Gets the product's placements, newest first, with their live steps.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductPlacementsQuery(slug), ct));

    /// <summary>Places the product's databases and private sites on a server.</summary>
    [HttpPost]
    [RequireActionHeader("placement")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Place([RegularExpression(Slug)] string slug, ProductPlaceApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductPlaceCommand
        {
            Product = slug,
            Server = request.Server,
            Database = request.Database,
            Sites = [.. (request.Sites ?? []).Select(site => new ProductPlacementSite(site.Site, site.Node))],
            Actor = actor.Actor,
        }, ct));
}
