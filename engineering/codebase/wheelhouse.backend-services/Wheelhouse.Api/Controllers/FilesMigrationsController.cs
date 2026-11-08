using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wheelhouse.Api.Auth;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.FilesMigrations.Commands;
using Wheelhouse.Application.Deployments.FilesMigrations.Queries;
using Wheelhouse.Application.Deployments.FilesMigrations.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Moves a product's legacy files into a Files host (dev targets) and cancels the job. Signed-in owner only: an
/// integration key is refused, and no MCP tool reaches it.</summary>
[ApiController]
[Route("api/deployments/targets/{target}")]
[Authorize(Policy = AuthConfigurationExtensions.OwnerSessionPolicy)]
public sealed class FilesMigrationsController(ISender sender, IActorContext actor) : ControllerBase
{
    private const string Slug = FormatPatternConstants.NameSlug;

    /// <summary>Reads what the target's files migration can move: the sources the manifests of the release it runs list,
    /// and the Files targets of its environment with their https URLs. Refused (409) for prod and Wheelhouse's own
    /// targets and when the release has no manifest.</summary>
    [HttpGet("files-migrations/manifest")]
    public async Task<IActionResult> Manifest([RegularExpression(Slug)] string target, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new FilesMigrationManifestQuery(target), ct));

    /// <summary>Queues a plan, push or verify of the product's legacy files as a job in the target's journal and answers
    /// it. Refused (409) for prod and Wheelhouse's own targets and when no manifest or Files credential exists; (400)
    /// for an unknown Files target, a URL off its app site, or no matching source; (502) when Files answers an error.</summary>
    [HttpPost("files-migrations")]
    [RequireActionHeader("control")]
    public async Task<IActionResult> Start([RegularExpression(Slug)] string target, FilesMigrationRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(new FilesMigrationCommand(
            target, request.Step!.Trim().ToLowerInvariant(), request.Wave, request.Sources, request.FilesTarget!, request.FilesUrl!,
            request.Import, actor.Actor), ct);
        return Render(result, envelope => Accepted(envelope));
    }

    /// <summary>Cancels a running files migration job; the runner records the outcome.</summary>
    [HttpPost("controls/{job:guid}/cancel")]
    [RequireActionHeader("control")]
    public async Task<IActionResult> Cancel([RegularExpression(Slug)] string target, Guid job, CancellationToken ct) =>
        Render(await sender.SendAsync(new FilesMigrationCancelCommand(target, job, actor.Actor), ct), null);

    private IActionResult Render(AppResult<System.Text.Json.JsonElement> result, Func<object, IActionResult>? accepted)
    {
        if (result is AppResult<System.Text.Json.JsonElement>.Failure { Error: var error } && FilesImportErrors.Is(error))
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway, Title = "Bad gateway", Detail = error.Message,
            });
        return accepted is null ? this.ToActionResult(result) : this.ToActionResult(result, envelope => accepted(envelope));
    }
}
