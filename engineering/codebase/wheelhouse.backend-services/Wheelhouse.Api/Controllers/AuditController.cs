using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Contracts.Paging;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Reads the append-only record of actions; nothing here changes it.</summary>
[ApiController]
[Route("api/audit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuditController(ISender sender) : ControllerBase
{
    /// <summary>Pages the entries newest first: <c>pageToken</c> continues a previous page's <c>nextPageToken</c>,
    /// <c>pageSize</c> is 1-200 (50 without it) and <paramref name="product"/> keeps the entries about one product, by
    /// slug.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] TokenPageApiRequest page, [FromQuery, StringLength(100)] string? product = null,
        CancellationToken ct = default) =>
        this.ToActionResult(await sender.SendAsync(
            new AuditListQuery(page.PageToken, page.PageSize ?? AuditListQuery.DefaultPageSize, product), ct));

    /// <summary>Recomputes the chain and reports whether every entry and link verifies.</summary>
    [HttpGet("verification")]
    public async Task<IActionResult> Verify(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new AuditVerifyQuery(), ct));
}
