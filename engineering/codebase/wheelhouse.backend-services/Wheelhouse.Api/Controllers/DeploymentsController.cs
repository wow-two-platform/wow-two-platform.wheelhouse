using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Wheelhouse.Api.Requests;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments;
using Wheelhouse.Application.Deployments.Automation.Commands;
using Wheelhouse.Application.Deployments.Automation.Queries;
using Wheelhouse.Application.Deployments.Freezes.Commands;
using Wheelhouse.Application.Deployments.Freezes.Queries;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Operations;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Wheelhouse.Api.Controllers;

/// <summary>Submits reviewed releases and observes target-owned deployment outcomes.</summary>
[ApiController]
[Route("api/deployments")]
public sealed class DeploymentsController(ISender sender, IActorContext actor) : ControllerBase
{
    // Route regex constraints ignore case; a validated parameter keeps catalog ids exact.
    private const string Slug = Wheelhouse.Application.Inventory.Constants.InventoryPatternConstants.Slug;
    private const string Branch = "^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$";

    /// <summary>Lists recent deployments with their last observed outcome.</summary>
    [HttpGet]
    public async Task<IActionResult> History(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("jobs"), ct));

    /// <summary>Pages the deployments a filter picks, newest first: workspaces, products and <c>product/service</c>
    /// pairs combine as a union, then status and range narrow; <c>cursor</c> continues a previous page.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> HistoryPage(
        [FromQuery] DeploymentFilterApiRequest filter, [FromQuery] string? cursor, [FromQuery] int limit = 25,
        CancellationToken ct = default) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentHistoryQuery(filter.ToFilter(), cursor, limit), ct));

    /// <summary>Counts the filter's finished rollouts per day in the IANA time zone <c>tz</c>; without a range, the last
    /// six months.</summary>
    [HttpGet("history/activity")]
    public async Task<IActionResult> Activity(
        [FromQuery] DeploymentFilterApiRequest filter, [FromQuery] string tz = "UTC", CancellationToken ct = default) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentActivityQuery(filter.ToFilter(), tz), ct));

    /// <summary>Measures the filter's entities over its range (the last 30 days without one) beside the range before
    /// it; the status filter does not apply.</summary>
    [HttpGet("history/stats")]
    public async Task<IActionResult> HistoryStats([FromQuery] DeploymentFilterApiRequest filter, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentStatsQuery(filter.ToFilter()), ct));

    /// <summary>Lists live rollouts with the step each is on; the filter's entities apply.</summary>
    [HttpGet("running")]
    public async Task<IActionResult> Running([FromQuery] DeploymentFilterApiRequest filter, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentRunningQuery(filter.ToFilter()), ct));

    /// <summary>Lists the targets that need attention: a latest rollout that failed or rolled back with no success since,
    /// or a rollout live for over 30 minutes; the filter's entities apply.</summary>
    [HttpGet("issues")]
    public async Task<IActionResult> Issues([FromQuery] DeploymentFilterApiRequest filter, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentIssuesQuery(filter.ToFilter()), ct));

    /// <summary>Lists the products whose newest finished main build failed, read from GitHub Actions at most every two
    /// minutes; a later success clears one. The filter's entities apply.</summary>
    [HttpGet("issues/builds")]
    public async Task<IActionResult> BuildFailures([FromQuery] DeploymentFilterApiRequest filter, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentBuildFailuresQuery(filter.ToFilter()), ct));

    /// <summary>Lists, per product, the newest release that succeeded on dev and is not what prod runs.</summary>
    [HttpGet("promotions")]
    public async Task<IActionResult> Promotions([FromQuery] DeploymentFilterApiRequest filter, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentPromotionsQuery(filter.ToFilter()), ct));

    /// <summary>Lists every product's automation choices — auto-deploy each new release to dev, auto-promote to prod —
    /// and the automatic deploys waiting for their targets.</summary>
    [HttpGet("automation")]
    public async Task<IActionResult> Automation(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentAutomationQuery(), ct));

    /// <summary>Changes what the control plane deploys of one product on its own; <c>{product}</c> is its id or slug.</summary>
    [HttpPut("automation/{product}")]
    [RequireActionHeader("automation")]
    public async Task<IActionResult> UpdateAutomation(string product, ProductAutomationRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new ProductAutomationUpdateCommand
        {
            Product = product, AutoDeployDev = request.AutoDeployDev, AutoPromote = request.AutoPromote,
        }, ct));

    /// <summary>Lists the holds on deploys: everywhere, per product, or per product environment.</summary>
    [HttpGet("freezes")]
    public async Task<IActionResult> Freezes(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeployFreezesQuery(), ct));

    /// <summary>Holds deploys: without a product everywhere, with one that product, with an environment too only there.
    /// Automatic deploys skip a frozen target; an owner's deploy needs an override with a reason.</summary>
    [HttpPost("freezes")]
    [RequireActionHeader("freeze")]
    public async Task<IActionResult> Freeze(DeployFreezeRequest request, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeployFreezeCreateCommand
        {
            Product = request.Product, Environment = request.Environment, Reason = request.Reason, Actor = actor.Actor,
        }, ct));

    /// <summary>Lifts the hold on one scope, named the way it was frozen; lifting an unfrozen scope changes nothing.</summary>
    [HttpDelete("freezes")]
    [RequireActionHeader("freeze")]
    public async Task<IActionResult> Unfreeze([FromQuery] string? product, [FromQuery] string? environment, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeployFreezeDeleteCommand { Product = product, Environment = environment }, ct),
            _ => NoContent());

    /// <summary>Reads one deployment in full from local records: services from and to, steps, sites, warnings and
    /// what a roll back restores. Never contacts the target.</summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentDetailQuery(id), ct));

    /// <summary>Lists provisioned deployment bindings.</summary>
    [HttpGet("targets")]
    public async Task<IActionResult> Targets(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("targets"), ct));

    /// <summary>Reads the release a target runs and whether it needs reconciliation.</summary>
    [HttpGet("targets/{target}/state")]
    public async Task<IActionResult> State([RegularExpression(Slug)] string target, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("state", target), ct));

    /// <summary>Reads allowlisted service relationships from the target's current release snapshot.</summary>
    [HttpGet("targets/{target}/topology")]
    public async Task<IActionResult> Topology([RegularExpression(Slug)] string target, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("topology", target), ct));

    /// <summary>Checks a target's readiness without changing it, optionally against one release.</summary>
    [HttpGet("targets/{target}/check")]
    public async Task<IActionResult> Check(
        [RegularExpression(Slug)] string target, [FromQuery, RegularExpression(Slug)] string? release, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentCheckQuery(target, release), ct));

    /// <summary>Acknowledges an interrupted or failed rollout the owner has inspected.</summary>
    [HttpPost("targets/{target}/reconcile")]
    [RequireActionHeader("reconcile")]
    public async Task<IActionResult> Reconcile(
        [RegularExpression(Slug)] string target, DeploymentReconcileRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(new DeploymentReconcileCommand(target, request.Job!.Value.ToString(), actor.Actor), ct);
        return this.ToActionResult(result);
    }

    /// <summary>Summarizes deployment outcomes, rollout time and recovery time over the last days.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery, Range(1, 90)] int days = 30, CancellationToken ct = default) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("stats", days.ToString(CultureInfo.InvariantCulture)), ct));

    /// <summary>Reads every server's host vitals and every target's container vitals without changing them.</summary>
    [HttpGet("vitals")]
    public async Task<IActionResult> Vitals(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("vitals"), ct));

    /// <summary>Reads stored vitals samples of the last hours (1-720): one target's, or every server's and target's.</summary>
    [HttpGet("vitals/history")]
    public async Task<IActionResult> VitalsHistory(
        [FromQuery, RegularExpression(Slug)] string? target, [FromQuery, Range(1, 720)] int hours = 24,
        CancellationToken ct = default) =>
        this.ToActionResult(await sender.SendAsync(new VitalsHistoryQuery(target, hours), ct));

    /// <summary>Lists published releases and per-commit builds from approved repositories.</summary>
    [HttpGet("releases")]
    public async Task<IActionResult> Releases(CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("releases"), ct));

    /// <summary>Reads what a release's bundle declares that a deploy should weigh — whether a failed rollout can restore
    /// the previous images, and whether it carries schema migrations — downloading it into the cache; no target is
    /// contacted.</summary>
    [HttpGet("releases/{release}/facts")]
    public async Task<IActionResult> ReleaseFacts([RegularExpression(Slug)] string release, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("facts", release), ct));

    /// <summary>Lists a product's branches, to choose which commits to build or deploy to dev.</summary>
    [HttpGet("products/{product}/branches")]
    public async Task<IActionResult> Branches([RegularExpression(Slug)] string product, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("branches", product), ct));

    /// <summary>Lists a branch's recent commits, each with its build when one exists.</summary>
    [HttpGet("products/{product}/commits")]
    public async Task<IActionResult> Commits(
        [RegularExpression(Slug)] string product, [FromQuery, Required, RegularExpression(Branch)] string branch, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentCommitsQuery(product, branch), ct));

    /// <summary>Reads what changed between what a target runs (<c>from</c>, omitted when it runs nothing) and the release
    /// to promote (<c>to</c>): the release bodies GitHub holds, newest first, and the compare commit list with the changed
    /// files that look like schema migrations.</summary>
    [HttpGet("products/{product}/changelog")]
    public async Task<IActionResult> Changelog(
        [RegularExpression(Slug)] string product, [FromQuery] string? from, [FromQuery, Required] string to, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentChangelogQuery(product, from, to), ct));

    /// <summary>Starts a build of a commit that has none; the build appears in the catalog once its workflow finishes.</summary>
    [HttpPost("products/{product}/builds")]
    [RequireActionHeader("build")]
    public async Task<IActionResult> Build(
        [RegularExpression(Slug)] string product, DeploymentBuildRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(new DeploymentBuildCommand(product, request.Commit, actor.Actor), ct);
        return this.ToActionResult(result, envelope => Accepted(envelope));
    }

    /// <summary>Reads the last lines one service's container wrote; the response is never cached or stored.</summary>
    [HttpGet("targets/{target}/services/{service}/logs")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Logs(
        [RegularExpression(Slug)] string target, [RegularExpression(Slug)] string service,
        [FromQuery, Range(1, 1000)] int tail = 200, CancellationToken ct = default) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentLogsQuery(target, service, tail), ct));

    /// <summary>Reads the authoritative outcome from the target.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct) =>
        this.ToActionResult(await sender.SendAsync(new DeploymentReadQuery("status", id.ToString()), ct));

    /// <summary>Queues a deployment; success is established by its later target status.</summary>
    [HttpPost]
    [RequireActionHeader("deploy")]
    public async Task<IActionResult> Start(DeploymentStartRequest request, CancellationToken ct)
    {
        var result = await sender.SendAsync(
            new DeploymentStartCommand(request.Target, request.Release, actor.Actor, request.Confirm, request.SkipDevPass,
                Reason: request.Reason, OverrideFreeze: request.OverrideFreeze), ct);
        return this.ToActionResult(result, envelope =>
            AcceptedAtAction(nameof(Status), new { id = envelope.Data.GetProperty("id").GetString() }, envelope));
    }
}
