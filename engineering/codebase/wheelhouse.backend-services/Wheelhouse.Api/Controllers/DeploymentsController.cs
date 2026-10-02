using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Wheelhouse.Api.Filters;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Deployments;
using Wheelhouse.Application.Operations;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;
using WoW.Two.Sdk.Backend.Beta.Web.Contracts;
using WoW.Two.Sdk.Backend.Beta.Web.ErrorMapping;

namespace Wheelhouse.Api.Controllers;

/// <summary>Submits reviewed releases and observes target-owned deployment outcomes.</summary>
[ApiController]
[Route("api/deployments")]
public sealed class DeploymentsController(ISender sender, IErrorHttpStatusCodeMapper errors) : ControllerBase
{
    // Route regex constraints ignore case; a validated parameter keeps catalog ids exact.
    private const string Slug = Wheelhouse.Application.Inventory.Constants.InventoryPatternConstants.Slug;
    private const string Branch = "^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$";

    /// <summary>Lists recent deployments with their last observed outcome.</summary>
    [HttpGet]
    public async Task<IActionResult> History(CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentReadQuery("jobs"), ct));

    /// <summary>Lists provisioned deployment bindings.</summary>
    [HttpGet("targets")]
    public async Task<IActionResult> Targets(CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentReadQuery("targets"), ct));

    /// <summary>Reads the release a target runs and whether it needs reconciliation.</summary>
    [HttpGet("targets/{target}/state")]
    public async Task<IActionResult> State([RegularExpression(Slug)] string target, CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentReadQuery("state", target), ct));

    /// <summary>Reads allowlisted service relationships from the target's current release snapshot.</summary>
    [HttpGet("targets/{target}/topology")]
    public async Task<IActionResult> Topology([RegularExpression(Slug)] string target, CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentReadQuery("topology", target), ct));

    /// <summary>Checks a target's readiness without changing it, optionally against one release.</summary>
    [HttpGet("targets/{target}/check")]
    public async Task<IActionResult> Check(
        [RegularExpression(Slug)] string target, [FromQuery, RegularExpression(Slug)] string? release, CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentCheckQuery(target, release), ct));

    /// <summary>Acknowledges an interrupted or failed rollout the operator has inspected.</summary>
    [HttpPost("targets/{target}/reconcile")]
    [RequireAction("reconcile")]
    public async Task<IActionResult> Reconcile(
        [RegularExpression(Slug)] string target, DeploymentReconcileRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(new DeploymentReconcileCommand(target, request.Job!.Value.ToString(), Actor()), ct);
        return Render(result);
    }

    /// <summary>Summarizes deployment outcomes, rollout time and recovery time over the last days.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery, Range(1, 90)] int days = 30, CancellationToken ct = default) =>
        Render(await sender.SendAsync(new DeploymentReadQuery("stats", days.ToString(CultureInfo.InvariantCulture)), ct));

    /// <summary>Reads every target's host and container vitals without changing them.</summary>
    [HttpGet("vitals")]
    public async Task<IActionResult> Vitals(CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentReadQuery("vitals"), ct));

    /// <summary>Reads stored vitals samples of the last hours (1-720), for one target or every target.</summary>
    [HttpGet("vitals/history")]
    public async Task<IActionResult> VitalsHistory(
        [FromQuery, RegularExpression(Slug)] string? target, [FromQuery, Range(1, 720)] int hours = 24,
        CancellationToken ct = default) =>
        (await sender.SendAsync(new VitalsHistoryQuery(target, hours), ct)).Match<IActionResult>(
            ok => Ok(ApiResponse<IReadOnlyList<VitalsSampleDto>>.Ok(ok.Data)),
            fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));

    /// <summary>Lists published releases and per-commit builds from approved repositories.</summary>
    [HttpGet("releases")]
    public async Task<IActionResult> Releases(CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentReadQuery("releases"), ct));

    /// <summary>Lists a product's branches, to choose which commits to build or deploy to dev.</summary>
    [HttpGet("products/{product}/branches")]
    public async Task<IActionResult> Branches([RegularExpression(Slug)] string product, CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentReadQuery("branches", product), ct));

    /// <summary>Lists a branch's recent commits, each with its build when one exists.</summary>
    [HttpGet("products/{product}/commits")]
    public async Task<IActionResult> Commits(
        [RegularExpression(Slug)] string product, [FromQuery, Required, RegularExpression(Branch)] string branch, CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentCommitsQuery(product, branch), ct));

    /// <summary>Starts a build of a commit that has none; the build appears in the catalog once its workflow finishes.</summary>
    [HttpPost("products/{product}/builds")]
    [RequireAction("build")]
    public async Task<IActionResult> Build(
        [RegularExpression(Slug)] string product, DeploymentBuildRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(new DeploymentBuildCommand(product, request.Commit, Actor()), ct);
        return result.Match<IActionResult>(
            ok => Accepted(ApiResponse<JsonElement>.Ok(ok.Data)),
            fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));
    }

    /// <summary>Reads the last lines one service's container wrote; the response is never cached or stored.</summary>
    [HttpGet("targets/{target}/services/{service}/logs")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Logs(
        [RegularExpression(Slug)] string target, [RegularExpression(Slug)] string service,
        [FromQuery, Range(1, 1000)] int tail = 200, CancellationToken ct = default) =>
        Render(await sender.SendAsync(new DeploymentLogsQuery(target, service, tail), ct));

    /// <summary>Reads the authoritative outcome from the target.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct) =>
        Render(await sender.SendAsync(new DeploymentReadQuery("status", id.ToString()), ct));

    /// <summary>Queues a deployment; success is established by its later target status.</summary>
    [HttpPost]
    [RequireAction("deploy")]
    public async Task<IActionResult> Start(DeploymentStartRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(
            new DeploymentStartCommand(request.Target, request.Release, Actor(), request.Confirm, request.SkipTestPass), ct);
        return result.Match<IActionResult>(
            ok => AcceptedAtAction(nameof(Status), new { id = ok.Data.GetProperty("id").GetString() },
                ApiResponse<JsonElement>.Ok(ok.Data)),
            fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));
    }

    private string Actor() => User.Identity?.Name ?? User.FindFirst("wt:username")?.Value ?? "authenticated-admin";

    private IActionResult Render(AppResult<JsonElement> result) => result.Match<IActionResult>(
        ok => Ok(ApiResponse<JsonElement>.Ok(ok.Data)),
        fail => Problem(statusCode: errors.ToStatusCode(fail.Error), detail: fail.Error.Message));
}
