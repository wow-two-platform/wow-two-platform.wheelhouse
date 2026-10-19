using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes the portfolio's products over HTTP.</summary>
[ApiController]
[Route("api/products")]
public sealed class ProductsController(ISender sender) : ControllerBase
{
    // Route regex constraints ignore case; a validated parameter keeps slugs exact.
    private const string Slug = InventoryPatternConstants.Slug;

    /// <summary>Gets every product.</summary>
    [HttpGet]
    [Authorize(Policy = AuthConfigurationExtensions.ProductsReadPolicy)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ProductDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductGetAllQuery(), ct));

    /// <summary>Gets one product by its slug.</summary>
    [HttpGet("{slug}")]
    [Authorize(Policy = AuthConfigurationExtensions.ProductsReadPolicy)]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductGetBySlugQuery { Slug = slug }, ct));

    /// <summary>Gets the icon a product's repository carries for a theme, form and color, falling back along the icon
    /// convention; not found when no file resolves, so the caller shows its monogram.</summary>
    [HttpGet("{slug}/icon")]
    [Authorize(Policy = AuthConfigurationExtensions.ProductsReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetIcon(
        [RegularExpression(Slug)] string slug, [FromQuery] ProductIconApiRequest request, CancellationToken ct) =>
        IconAsync(slug, null, request, ct);

    /// <summary>Gets the icon one of a product's services carries, falling back to the product's own icon along the
    /// icon convention; not found when no file resolves.</summary>
    [HttpGet("{slug}/services/{service}/icon")]
    [Authorize(Policy = AuthConfigurationExtensions.ProductsReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetServiceIcon(
        [RegularExpression(Slug)] string slug,
        [RegularExpression(Slug)] string service,
        [FromQuery] ProductIconApiRequest request,
        CancellationToken ct) =>
        IconAsync(slug, service, request, ct);

    /// <summary>Adds a product to the portfolio; it starts active, in the workspace the body names or the default one.</summary>
    [HttpPost]
    [RequireActionHeader("product")]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateProductApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductCreateCommand
        {
            Slug = request.Slug,
            Name = request.Name,
            Description = request.Description,
            Repository = request.Repository,
            DefaultBranch = request.DefaultBranch,
            Release = request.Release,
            Deploy = request.Deploy,
            WorkspaceId = request.WorkspaceId,
        }, ct), envelope => CreatedAtAction(nameof(GetBySlug), new { slug = envelope.Data.Slug }, envelope));

    /// <summary>Changes a product's definition; its slug stays.</summary>
    [HttpPut("{slug}")]
    [RequireActionHeader("product")]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [RegularExpression(Slug)] string slug, UpdateProductApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductUpdateCommand
        {
            Slug = slug,
            Name = request.Name,
            Description = request.Description,
            Repository = request.Repository,
            DefaultBranch = request.DefaultBranch,
            Release = request.Release,
            Deploy = request.Deploy,
        }, ct));

    /// <summary>Reads the product's manifest again now: checks the repository's branch head without waiting for the
    /// remembered one, and re-reads <c>product/product.json</c> when the head is a commit the cache has not seen.
    /// Answers with the product; 503 when the branch cannot be read from GitHub.</summary>
    [HttpPost("{slug}/identity/refresh")]
    [RequireActionHeader("refresh")]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RefreshIdentity([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductIdentityRefreshCommand { Slug = slug }, ct));

    /// <summary>Removes a product no target runs any more.</summary>
    [HttpDelete("{slug}")]
    [RequireActionHeader("product")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductDeleteCommand { Slug = slug }, ct), _ => NoContent());

    /// <summary>Moves a product into another workspace. The route names the product by its identifier or its slug.</summary>
    [HttpPut("{product}/workspace")]
    [RequireActionHeader("workspace")]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateWorkspace(
        string product, UpdateProductWorkspaceApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(
            new ProductWorkspaceUpdateCommand { Product = product, WorkspaceId = request.WorkspaceId }, ct));

    /// <summary>Marks a product active or inactive; inactive only hides it from the console's lists, so it still deploys
    /// and reads. The route names the product by its identifier or its slug.</summary>
    [HttpPut("{product}/status")]
    [RequireActionHeader("status")]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(string product, UpdateStatusApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductStatusUpdateCommand { Product = product, Status = request.Status }, ct));

    /// <summary>Marks one of a product's services active or inactive in every environment; a service never marked is
    /// active. The route names the product by its identifier or its slug, and the service by its <c>deploy.yml</c> key.</summary>
    [HttpPut("{product}/services/{service}/status")]
    [RequireActionHeader("status")]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateServiceStatus(
        string product, string service, UpdateStatusApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(
            new ServiceStatusUpdateCommand { Product = product, Service = service, Status = request.Status }, ct));

    private async Task<IActionResult> IconAsync(string slug, string? service, ProductIconApiRequest request, CancellationToken ct) =>
        (await sender.SendAsync(new ProductIconQuery
        {
            Slug = slug,
            Icon = new ProductIconRequest { Service = service, Theme = request.Theme, Form = request.Form, Color = request.Color },
        }, ct)).Match<IActionResult>(
            ok =>
            {
                Response.Headers.CacheControl = "private, max-age=3600";
                Response.Headers.XContentTypeOptions = "nosniff";
                // An <img> ignores the disposition; opening the URL on its own downloads the file instead of
                // rendering it, so an SVG from a repository can never run script on this origin.
                return File(ok.Data.Content, ok.Data.ContentType, "icon" + Path.GetExtension(ok.Data.Path));
            },
            fail => this.ToProblem(fail.Error));
}
