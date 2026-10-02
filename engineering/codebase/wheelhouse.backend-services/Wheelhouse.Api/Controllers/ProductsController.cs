using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Filters;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Products.Commands;
using Wheelhouse.Application.Products.Models;
using Wheelhouse.Application.Products.Queries;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;
using WoW.Two.Sdk.Backend.Beta.Web.Contracts;
using WoW.Two.Sdk.Backend.Beta.Web.ErrorMapping;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes the portfolio's products over HTTP.</summary>
[ApiController]
[Route("api/products")]
public sealed class ProductsController(ISender sender, IErrorHttpStatusCodeMapper errorMapper) : ControllerBase
{
    // Route regex constraints ignore case; a validated parameter keeps slugs exact.
    private const string Slug = InventoryPatternConstants.Slug;

    /// <summary>Gets every product.</summary>
    [HttpGet]
    [Authorize(Policy = AuthConfigurationExtensions.ProductsReadPolicy)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ProductDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        (await sender.SendAsync(new ProductGetAllQuery(), ct)).Match<IActionResult>(
            ok => Ok(ApiResponse<IReadOnlyList<ProductDto>>.Ok(ok.Data)),
            fail => Problem(detail: fail.Error.Message, statusCode: errorMapper.ToStatusCode(fail.Error)));

    /// <summary>Gets one product by its slug.</summary>
    [HttpGet("{slug}")]
    [Authorize(Policy = AuthConfigurationExtensions.ProductsReadPolicy)]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        (await sender.SendAsync(new ProductGetBySlugQuery { Slug = slug }, ct)).Match<IActionResult>(
            ok => Ok(ApiResponse<ProductDto>.Ok(ok.Data)),
            fail => Problem(detail: fail.Error.Message, statusCode: errorMapper.ToStatusCode(fail.Error)));

    /// <summary>Gets the icon a product's repository carries; not found when it carries none.</summary>
    [HttpGet("{slug}/icon")]
    [Authorize(Policy = AuthConfigurationExtensions.ProductsReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIcon([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        (await sender.SendAsync(new ProductIconQuery { Slug = slug }, ct)).Match<IActionResult>(
            ok =>
            {
                Response.Headers.CacheControl = "private, max-age=3600";
                Response.Headers.XContentTypeOptions = "nosniff";
                // An <img> ignores the disposition; opening the URL on its own downloads the file instead of
                // rendering it, so an SVG from a repository can never run script on this origin.
                return File(ok.Data.Content, ok.Data.ContentType, "icon" + Path.GetExtension(ok.Data.Path));
            },
            fail => Problem(detail: fail.Error.Message, statusCode: errorMapper.ToStatusCode(fail.Error)));

    /// <summary>Adds a product to the portfolio; it starts as building.</summary>
    [HttpPost]
    [RequireAction("product")]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateProductApiRequest request, CancellationToken ct) =>
        (await sender.SendAsync(new ProductCreateCommand
        {
            Slug = request.Slug,
            Name = request.Name,
            Description = request.Description,
            Repository = request.Repository,
            DefaultBranch = request.DefaultBranch,
            Release = request.Release,
        }, ct)).Match<IActionResult>(
            ok => CreatedAtAction(nameof(GetBySlug), new { slug = ok.Data.Slug }, ApiResponse<ProductDto>.Ok(ok.Data)),
            fail => Problem(detail: fail.Error.Message, statusCode: errorMapper.ToStatusCode(fail.Error)));

    /// <summary>Changes a product's definition; its slug stays.</summary>
    [HttpPut("{slug}")]
    [RequireAction("product")]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [RegularExpression(Slug)] string slug, UpdateProductApiRequest request, CancellationToken ct) =>
        (await sender.SendAsync(new ProductUpdateCommand
        {
            Slug = slug,
            Name = request.Name,
            Description = request.Description,
            Repository = request.Repository,
            DefaultBranch = request.DefaultBranch,
            Release = request.Release,
        }, ct)).Match<IActionResult>(
            ok => Ok(ApiResponse<ProductDto>.Ok(ok.Data)),
            fail => Problem(detail: fail.Error.Message, statusCode: errorMapper.ToStatusCode(fail.Error)));

    /// <summary>Removes a product no target runs any more.</summary>
    [HttpDelete("{slug}")]
    [RequireAction("product")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([RegularExpression(Slug)] string slug, CancellationToken ct) =>
        (await sender.SendAsync(new ProductDeleteCommand { Slug = slug }, ct)).Match<IActionResult>(
            _ => NoContent(),
            fail => Problem(detail: fail.Error.Message, statusCode: errorMapper.ToStatusCode(fail.Error)));

    /// <summary>Records where a product stands in the portfolio.</summary>
    [HttpPut("{slug}/lifecycle")]
    [RequireAction("lifecycle")]
    [ProducesResponseType<ApiResponse<ProductDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLifecycle(
        [RegularExpression(Slug)] string slug, UpdateProductLifecycleApiRequest request, CancellationToken ct) =>
        (await sender.SendAsync(new ProductLifecycleUpdateCommand { Slug = slug, Lifecycle = request.Lifecycle }, ct))
            .Match<IActionResult>(
                ok => Ok(ApiResponse<ProductDto>.Ok(ok.Data)),
                fail => Problem(detail: fail.Error.Message, statusCode: errorMapper.ToStatusCode(fail.Error)));
}
