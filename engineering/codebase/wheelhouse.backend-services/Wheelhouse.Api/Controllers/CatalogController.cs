using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Catalog.Commands;
using Wheelhouse.Application.Catalog.Models;
using Wheelhouse.Application.Catalog.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Exposes the wow2 catalog — one YAML file in a GitHub repository, read with the owner's token — and imports
/// its entries as workspaces and products. The owner's session only.</summary>
[ApiController]
[Route("api/catalog")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CatalogController(ISender sender) : ControllerBase
{
    /// <summary>Reads the catalog at its ref's newest commit, with the product each entry already is. 503 when GitHub
    /// cannot serve it, 404 when the file is missing, 422 naming the line and field when it is malformed.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<CatalogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new CatalogGetQuery(), ct));

    /// <summary>Imports entries: creates the workspaces their products need, creates a product for each entry Wheelhouse
    /// deploys, and moves existing products into their entry's workspace. Audited as <c>catalog.import</c>.</summary>
    [HttpPost("import")]
    [RequireActionHeader("catalog")]
    [ProducesResponseType<ApiResponse<CatalogImportResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Import(CatalogImportApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new CatalogImportCommand { Slugs = request.Slugs }, ct));
}
