using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Application.Portfolio.Models;
using Wheelhouse.Application.Portfolio.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Serves the Products page's portfolio-wide reads. The owner's session only, like every deployment read.</summary>
[ApiController]
[Route("api/portfolio")]
public sealed class PortfolioController(ISender sender) : ControllerBase
{
    /// <summary>Summarizes every product's deployments: per environment the latest rollout (refused ones skipped), the
    /// last success and finished rollouts per week; per workspace succeeded and failed rollouts per week. Weeks start on
    /// Monday in the IANA time zone <c>tz</c>, the current one last; <c>to</c> sets the instant it contains.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<ApiResponse<PortfolioSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Summary(
        [FromQuery] int weeks = PortfolioSummaryQuery.DefaultWeeks, [FromQuery] string tz = "UTC",
        [FromQuery] DateTimeOffset? to = null, CancellationToken ct = default) =>
        this.ToActionResult(await sender.SendAsync(new PortfolioSummaryQuery(weeks, tz, to), ct));
}
