using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Application.Audit;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Reads the append-only record of operator actions; nothing here changes it.</summary>
[ApiController]
[Route("api/audit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuditController(ISender sender) : ControllerBase
{
    /// <summary>Lists the newest entries; <paramref name="before"/> pages back from a sequence number.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, Range(1, 200)] int limit = 50, [FromQuery, Range(1, long.MaxValue)] long? before = null,
        CancellationToken ct = default) =>
        this.ToActionResult(await sender.SendAsync(new AuditListQuery(limit, before), ct));

    /// <summary>Recomputes the chain and reports whether every entry and link verifies.</summary>
    [HttpGet("verification")]
    public async Task<IActionResult> Verify(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new AuditVerifyQuery(), ct));
}
