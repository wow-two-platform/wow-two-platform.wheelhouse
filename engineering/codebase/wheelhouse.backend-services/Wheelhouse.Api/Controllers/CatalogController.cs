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

/// <summary>Exposes the catalog — one YAML file in each hub repository <c>Catalog:Sources</c> lists, read with the owner's
/// token and merged — and imports its entries as workspaces and products. The owner's session only.</summary>
[ApiController]
[Route("api/catalog")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CatalogController(ISender sender) : ControllerBase
{
    /// <summary>Reads every hub at its ref's newest commit and merges them, with the product each entry already is. A hub
    /// GitHub cannot serve, without the file, malformed, or repeating an earlier hub's slug carries its error in
    /// <c>sources</c> and lists nothing; the others still list theirs.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<CatalogDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new CatalogGetQuery(), ct));

    /// <summary>Imports entries from any readable hub: creates the workspaces their products need, creates a product for
    /// each entry Wheelhouse deploys, and moves existing products into their entry's workspace. A slug no readable hub
    /// lists is 400, naming the hubs that could not be read. Audited as <c>catalog.import</c>.</summary>
    [HttpPost("import")]
    [RequireActionHeader("catalog")]
    [ProducesResponseType<ApiResponse<CatalogImportResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Import(CatalogImportApiRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new CatalogImportCommand { Slugs = request.Slugs }, ct));
}
