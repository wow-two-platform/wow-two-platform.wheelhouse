using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Wheelhouse.Application.Deployments;
using Wheelhouse.Application.Inventory.Constants;
using Wheelhouse.Application.Products.Queries;
using Wheelhouse.Application.Servers.Models;
using Wheelhouse.Application.Servers.Queries;
using Wheelhouse.Application.Targets.Queries;
using Wheelhouse.Infrastructure.Settings;
using Wheelhouse.Persistence;
using WoW.Two.Sdk.Backend.Beta.Mediator;
using WoW.Two.Sdk.Backend.Beta.Mediator.Result;

namespace Wheelhouse.Api.Mcp;

/// <summary>Reads portfolio and fleet state without exposing credentials or mutation operations.</summary>
[McpServerToolType]
public sealed partial class WheelhouseReadTools(ISender sender, WheelhouseDbContext database, DeploymentSettings deployment)
{
    /// <summary>Reads project identities, lifecycle and public site metadata.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The product catalog with secret namespaces omitted.</returns>
    [McpServerTool(Name = "list_projects", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List Wheelhouse projects (products), their repositories, lifecycle and environment sites. Omits secret metadata.")]
    [Authorize(Policy = WheelhouseMcpConfiguration.CatalogPolicy)]
    public async Task<object> ListProjects(CancellationToken ct)
    {
        var products = Read(await sender.SendAsync(new ProductGetAllQuery(), ct));
        return products.Select(product => new
        {
            product.Slug, product.Name, product.Description, product.Lifecycle, product.Repository, product.Release,
            Environments = product.Environments.Select(environment => new { environment.Name, environment.Sites })
        }).ToArray();
    }

    /// <summary>Reads registered servers without contacting them.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The server inventory without credentials.</returns>
    [McpServerTool(Name = "list_servers", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List registered servers, addresses, providers and regions. Does not probe hosts or return credentials.")]
    [Authorize(Policy = WheelhouseMcpConfiguration.DeploymentsPolicy)]
    public async Task<IReadOnlyList<ServerDto>> ListServers(CancellationToken ct) =>
        Read(await sender.SendAsync(new ServerGetAllQuery(), ct));

    /// <summary>Reads deployment bindings without settings or credential paths.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The target bindings.</returns>
    [McpServerTool(Name = "list_targets", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List deployment target IDs and their product, server, environment and sites. Omits settings paths.")]
    [Authorize(Policy = WheelhouseMcpConfiguration.DeploymentsPolicy)]
    public async Task<object> ListTargets(CancellationToken ct)
    {
        var targets = Read(await sender.SendAsync(new TargetGetAllQuery(), ct));
        return targets.Select(target => new { target.Slug, target.Product, target.Server, target.Environment, target.Sites }).ToArray();
    }

    /// <summary>Reads deployment history observed by the runner.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The deployment history.</returns>
    [McpServerTool(Name = "list_deployments", ReadOnly = true, Destructive = false, OpenWorld = true)]
    [Description("Read recent deployment outcomes. Queued or observed state does not establish a successful deployment.")]
    [Authorize(Policy = WheelhouseMcpConfiguration.DeploymentsPolicy)]
    public async Task<JsonElement> ListDeployments(CancellationToken ct) =>
        Read(await sender.SendAsync(new DeploymentReadQuery("jobs"), ct));

    /// <summary>Reads a target's current release and reconciliation state.</summary>
    /// <param name="target">The target slug returned by list_targets.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The target's observed state.</returns>
    [McpServerTool(Name = "get_deployment_state", ReadOnly = true, Destructive = false, OpenWorld = true)]
    [Description("Read the current release and reconciliation state of an existing target.")]
    [Authorize(Policy = WheelhouseMcpConfiguration.DeploymentsPolicy)]
    public async Task<JsonElement> GetDeploymentState(string target, CancellationToken ct)
    {
        ValidateTarget(target);
        return Read(await sender.SendAsync(new DeploymentReadQuery("state", target), ct));
    }

    /// <summary>Checks an existing target without deploying or reconciling it.</summary>
    /// <param name="target">The target slug returned by list_targets.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The runner's readiness checks.</returns>
    [McpServerTool(Name = "check_target", ReadOnly = true, Destructive = false, OpenWorld = true)]
    [Description("Run read-only readiness checks for an existing target. Does not build, deploy or reconcile.")]
    [Authorize(Policy = WheelhouseMcpConfiguration.DeploymentsPolicy)]
    public async Task<JsonElement> CheckTarget(string target, CancellationToken ct)
    {
        ValidateTarget(target);
        return Read(await sender.SendAsync(new DeploymentCheckQuery(target, null), ct));
    }

    /// <summary>Reads current host and container vitals through the runner.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The current fleet vitals.</returns>
    [McpServerTool(Name = "get_vitals", ReadOnly = true, Destructive = false, OpenWorld = true)]
    [Description("Read current fleet host and container vitals. Results are observations, not retained monitoring guarantees.")]
    [Authorize(Policy = WheelhouseMcpConfiguration.DeploymentsPolicy)]
    public async Task<JsonElement> GetVitals(CancellationToken ct) =>
        Read(await sender.SendAsync(new DeploymentReadQuery("vitals"), ct));

    /// <summary>Reads control-plane database readiness and build identity.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The control-plane readiness and rehearsal marker.</returns>
    [McpServerTool(Name = "get_health", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read Wheelhouse database readiness, build version and whether this instance uses the local rehearsal rig.")]
    [Authorize(Policy = WheelhouseMcpConfiguration.DeploymentsPolicy)]
    public async Task<object> GetHealth(CancellationToken ct) => new
    {
        Service = "Wheelhouse",
        DatabaseReady = await database.Database.CanConnectAsync(ct),
        Version = typeof(WheelhouseReadTools).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        LocalRig = deployment.IsLocalRig
    };

    private static T Read<T>(AppResult<T> result) where T : notnull => result.Match(
        ok => ok.Data,
        _ => throw new McpException("Wheelhouse could not complete this read. Inspect the operator console for details."));

    private static void ValidateTarget(string target)
    {
        if (target is null || !TargetSlug().IsMatch(target))
            throw new McpException("Target must be an existing lowercase target slug, at most 48 characters.");
    }

    [GeneratedRegex(InventoryPatternConstants.Slug + "\\z")]
    private static partial Regex TargetSlug();
}
