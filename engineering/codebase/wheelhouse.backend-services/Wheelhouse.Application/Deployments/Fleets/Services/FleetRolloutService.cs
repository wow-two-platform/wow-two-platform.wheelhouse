using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Audit;
using Wheelhouse.Application.Deployments.Fleets.Commands;
using Wheelhouse.Application.Deployments.Fleets.Models;
using Wheelhouse.Application.Deployments.Freezes.Services;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using Wheelhouse.Domain.Audit.Enums;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Servers.Enums;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Fleets.Services;

/// <summary>Promotes several products to prod in one run. A fleet takes releases Ready to promote offers, one per prod
/// target, and submits them one at a time in its order through the ordinary deploy command — so freezes, the runner's
/// gates, the reason and the audit trail apply to each, as the owner who started it. It waits for each rollout's
/// finished outcome, stops at the first failure or refusal (the rest are not started) and stops between products once
/// the owner cancels. One fleet runs at a time; <see cref="FleetRolloutGate"/> keeps a product from going twice.</summary>
public sealed partial class FleetRolloutService(
    IFleetRolloutsRepository fleets,
    ITargetsRepository targets,
    IServersRepository servers,
    DeployFreezeService freezes,
    DeploymentHistoryService history,
    IAuditTrail audit,
    BackgroundActor background,
    FleetRolloutGate gate,
    ISender sender,
    TimeProvider time,
    ILogger<FleetRolloutService> logger)
{
    /// <summary>Holds the most products one fleet promotes.</summary>
    public const int MaxItems = 20;

    /// <summary>Holds how long a submitted rollout may give no finished outcome before the fleet stops on it, as the
    /// Issues list calls a rollout stuck.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(30);

    /// <summary>Holds how long a submission may be missing from the runner's records before the fleet stops on it.</summary>
    public static readonly TimeSpan MissingAfter = TimeSpan.FromMinutes(2);

    /// <summary>Records a new fleet, every product waiting. Refuses while another fleet runs, a target that is not prod
    /// or not in the inventory, a release Ready to promote does not offer for its target, a local-server prod whose ID
    /// was not typed, and a frozen target without an override.</summary>
    /// <param name="command">The fleet to start.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The fleet, or why it was refused.</returns>
    public async Task<AppResult<FleetRolloutDto>> CreateAsync(FleetRolloutStartCommand command, CancellationToken ct)
    {
        if ((await fleets.GetRunningIdsAsync(ct)).Count > 0)
            return Fail(AppErrorFactory.Conflict("A fleet rollout is running; wait for it to end or cancel it."));

        var targetRows = (await targets.GetAllAsync(ct)).ToDictionary(row => row.Slug, StringComparer.Ordinal);
        var local = (await servers.GetAllAsync(ct)).Where(server => server.Provider == VpsProvider.Local)
            .Select(server => server.Id).ToHashSet();
        var offers = await sender.SendAsync(new DeploymentPromotionsQuery(new DeploymentFilter()), ct);
        if (offers is not AppResult<IReadOnlyList<DeploymentPromotionDto>>.Success { Data: var offered })
            return Fail(((AppResult<IReadOnlyList<DeploymentPromotionDto>>.Failure)offers).Error);

        var frozen = new List<string>();
        var items = new List<FleetRolloutItemEntity>();
        foreach (var (item, position) in command.Items.Select((item, index) => (item, index)))
        {
            if (!targetRows.TryGetValue(item.Target, out var target))
                return Fail(AppErrorFactory.Validation($"Target '{item.Target}' was not found."));
            if (target.Environment != DeploymentEnvironment.Prod)
                return Fail(AppErrorFactory.Validation($"{item.Target} is not a prod target; a fleet promotes to prod only."));
            if (local.Contains(target.ServerId) && item.Confirm != item.Target)
                return Fail(AppErrorFactory.Validation($"Type {item.Target} to promote prod on the local server."));
            var offer = offered.FirstOrDefault(row => row.ProdTargetId == item.Target && row.BundleId == item.Release);
            if (offer is null)
                return Fail(AppErrorFactory.Conflict(
                    $"{item.Release} is not ready to promote to {item.Target}: it has not passed dev, or prod already runs it or a later release."));
            if (await freezes.CoveringAsync(target.ProductId, DeploymentEnvironment.Prod, ct) is { } freeze)
                frozen.Add($"{item.Target} ({DeployFreezeService.Map(freeze, offer.Product).Covers})");
            items.Add(new FleetRolloutItemEntity
            {
                FleetId = command.Id, Position = position, Product = offer.Product, Target = item.Target,
                BundleId = item.Release, Release = offer.Release, FromRelease = offer.ProdRelease,
                Confirmed = item.Confirm == item.Target, Status = FleetItemStatus.Waiting,
            });
        }

        if (frozen.Count > 0 && !command.OverrideFreeze)
            return Fail(AppErrorFactory.Conflict(
                $"Deploys to {string.Join(", ", frozen)} are frozen. Override the freeze with a reason to promote anyway."));

        var fleet = new FleetRolloutEntity
        {
            Id = command.Id, Actor = command.Actor, OverrideFreeze = command.OverrideFreeze,
            Reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim(),
            Status = FleetRolloutStatus.Running, CreatedAt = time.GetUtcNow(), Items = items,
        };
        if (!await fleets.AddAsync(fleet, ct))
            return Fail(AppErrorFactory.Conflict("A fleet rollout is running; wait for it to end or cancel it."));
        return AppResult<FleetRolloutDto>.Ok(Map(fleet));
    }

    /// <summary>Moves one fleet on: records its current product's finished outcome, then submits the next product, or
    /// ends the fleet. A fleet that ended is answered as it is.</summary>
    /// <param name="id">The fleet.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The fleet as it now stands, or not found.</returns>
    public async Task<AppResult<FleetRolloutDto>> AdvanceAsync(Guid id, CancellationToken ct)
    {
        using var entered = await gate.EnterAsync(ct);
        var fleet = await fleets.GetAsync(id, ct);
        if (fleet is null)
            return Fail(AppErrorFactory.NotFound($"Fleet rollout '{id}' was not found."));
        await AdvanceAsync(fleet, ct);
        return AppResult<FleetRolloutDto>.Ok(Map(fleet));
    }

    /// <summary>Moves every running fleet on, as the background pass does.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>How many fleets were looked at.</returns>
    public async Task<int> AdvanceRunningAsync(CancellationToken ct)
    {
        var running = await fleets.GetRunningIdsAsync(ct);
        foreach (var id in running)
            await AdvanceAsync(id, ct);
        return running.Count;
    }

    /// <summary>Asks a running fleet to stop between products: the product it is on finishes, the rest are not started.
    /// A fleet between products stops at once.</summary>
    /// <param name="id">The fleet.</param>
    /// <param name="actor">Who cancels it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The fleet as it now stands, or why it cannot be cancelled.</returns>
    public async Task<AppResult<FleetRolloutDto>> CancelAsync(Guid id, string actor, CancellationToken ct)
    {
        using var entered = await gate.EnterAsync(ct);
        var fleet = await fleets.GetAsync(id, ct);
        if (fleet is null)
            return Fail(AppErrorFactory.NotFound($"Fleet rollout '{id}' was not found."));
        if (fleet.Status != FleetRolloutStatus.Running)
            return Fail(AppErrorFactory.Conflict("The fleet rollout already ended."));
        if (fleet.CancelRequestedAt is null)
        {
            fleet.CancelRequestedBy = actor;
            fleet.CancelRequestedAt = time.GetUtcNow();
            await fleets.SaveAsync(ct);
        }

        await AdvanceAsync(fleet, ct);
        return AppResult<FleetRolloutDto>.Ok(Map(fleet));
    }

    /// <summary>Maps a fleet.</summary>
    /// <param name="fleet">The stored fleet with its products.</param>
    /// <returns>The fleet as the API answers it.</returns>
    public static FleetRolloutDto Map(FleetRolloutEntity fleet) => new(
        fleet.Id, fleet.Actor, fleet.Reason, fleet.OverrideFreeze, Name(fleet.Status), fleet.CreatedAt, fleet.CompletedAt,
        fleet.CancelRequestedBy, fleet.CancelRequestedAt,
        [.. fleet.Items.OrderBy(item => item.Position).Select(item => new FleetRolloutItemDto(
            item.Position, item.Product, item.Target, item.BundleId, item.Release, item.FromRelease, Name(item.Status),
            item.DeploymentId, item.Detail, item.StartedAt, item.CompletedAt))]);

    /// <summary>The wire name of a fleet status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>Its snake_case name.</returns>
    public static string Name(FleetRolloutStatus status) => status.ToString().ToLowerInvariant();

    /// <summary>The wire name of a product's status in a fleet.</summary>
    /// <param name="status">The status.</param>
    /// <returns>Its snake_case name.</returns>
    public static string Name(FleetItemStatus status) =>
        status == FleetItemStatus.NotStarted ? "not_started" : status.ToString().ToLowerInvariant();

    /// <summary>Moves a fleet held under the gate on. Submissions act for the fleet's owner.</summary>
    private async Task AdvanceAsync(FleetRolloutEntity fleet, CancellationToken ct)
    {
        if (fleet.Status != FleetRolloutStatus.Running)
            return;
        // Outside a request the deploys and the fleet's end are audited as the owner who started it.
        background.Actor = fleet.Actor;
        DeploymentHistory? loaded = null;
        while (fleet.Status == FleetRolloutStatus.Running)
        {
            var now = time.GetUtcNow();
            if (fleet.Items.FirstOrDefault(item => item.Status == FleetItemStatus.Running) is { } current)
            {
                if (loaded is null)
                {
                    if (await history.LoadAsync(ct) is not AppResult<DeploymentHistory>.Success { Data: var read })
                    {
                        LogHistoryFailed(logger, fleet.Id);
                        return; // the next pass reads again
                    }

                    loaded = read;
                }

                if (!await SettleAsync(fleet, current, loaded, now, ct))
                    return; // still running
                continue;
            }

            if (fleet.CancelRequestedAt is not null)
            {
                await EndAsync(fleet, FleetRolloutStatus.Cancelled, now, ct);
                return;
            }

            if (fleet.Items.FirstOrDefault(item => item.Status == FleetItemStatus.Waiting) is not { } next)
            {
                await EndAsync(fleet, FleetRolloutStatus.Succeeded, now, ct);
                return;
            }

            await SubmitAsync(fleet, next, now, ct);
            if (next.Status == FleetItemStatus.Running)
                return; // its outcome comes later
        }
    }

    /// <summary>Records a running product's outcome once its rollout finished; false while it still runs.</summary>
    private async Task<bool> SettleAsync(
        FleetRolloutEntity fleet, FleetRolloutItemEntity current, DeploymentHistory loaded, DateTimeOffset now, CancellationToken ct)
    {
        var record = loaded.Entries.FirstOrDefault(entry => entry.Record.Id == current.DeploymentId)?.Record;
        var since = now - (current.StartedAt ?? now);
        if (record is null)
        {
            if (since < MissingAfter)
                return false;
            await StopAsync(fleet, current, FleetItemStatus.Failed, "Its submission is missing from the runner's records.", now, ct);
            return true;
        }

        var outcome = DeploymentOutcome.Of(record.Status);
        // A finished status without its completion is still being finalized: rolling back or cleaning up.
        var finished = record.CompletedAt is not null && outcome is DeploymentOutcome.Succeeded or DeploymentOutcome.Failed
            || outcome == DeploymentOutcome.Refused;
        if (!finished)
        {
            if (since < StuckAfter)
                return false;
            await StopAsync(fleet, current, FleetItemStatus.Failed,
                "No finished outcome after " + (int)StuckAfter.TotalMinutes + " minutes; check the target.", now, ct);
            return true;
        }

        current.CompletedAt = record.CompletedAt ?? now;
        if (outcome == DeploymentOutcome.Succeeded)
        {
            current.Status = FleetItemStatus.Succeeded;
            current.Detail = record.Warnings is { Count: > 0 } warnings
                ? warnings.Count + (warnings.Count == 1 ? " warning" : " warnings")
                : null;
            await fleets.SaveAsync(ct);
            return true;
        }

        var status = record.Status.Replace('_', ' ');
        var detail = char.ToUpperInvariant(status[0]) + status[1..] + (record.Reason is { Length: > 0 } reason ? ": " + reason : "");
        await StopAsync(fleet, current,
            outcome == DeploymentOutcome.Refused ? FleetItemStatus.Refused : FleetItemStatus.Failed, detail, current.CompletedAt.Value, ct);
        return true;
    }

    /// <summary>Submits one product through the ordinary deploy command; a refusal stops the fleet.</summary>
    private async Task SubmitAsync(FleetRolloutEntity fleet, FleetRolloutItemEntity next, DateTimeOffset now, CancellationToken ct)
    {
        var result = await sender.SendAsync(new DeploymentStartCommand(next.Target, next.BundleId, fleet.Actor,
            next.Confirmed ? next.Target : null, Reason: fleet.Reason, OverrideFreeze: fleet.OverrideFreeze, Fleet: fleet.Id), ct);
        if (result is AppResult<JsonElement>.Success { Data: var submitted }
            && submitted.ValueKind == JsonValueKind.Object && submitted.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String)
        {
            next.Status = FleetItemStatus.Running;
            next.DeploymentId = id.GetString();
            next.StartedAt = now;
            await fleets.SaveAsync(ct);
            LogSubmitted(logger, next.Release ?? next.BundleId, next.Target, fleet.Id);
            return;
        }

        var reason = result is AppResult<JsonElement>.Failure { Error: var error } ? error.Message : "The runner answered no deployment.";
        next.StartedAt = now;
        await StopAsync(fleet, next, FleetItemStatus.Refused, reason, now, ct);
    }

    /// <summary>Ends a product as failed or refused, and the fleet with it.</summary>
    private async Task StopAsync(
        FleetRolloutEntity fleet, FleetRolloutItemEntity item, FleetItemStatus status, string detail, DateTimeOffset now,
        CancellationToken ct)
    {
        item.Status = status;
        item.Detail = detail.Length > 300 ? detail[..300] : detail;
        item.CompletedAt ??= now;
        await EndAsync(fleet, FleetRolloutStatus.Failed, now, ct);
    }

    /// <summary>Ends the fleet: the products still waiting are not started; the audit trail records how it went.</summary>
    private async Task EndAsync(FleetRolloutEntity fleet, FleetRolloutStatus status, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var item in fleet.Items.Where(item => item.Status == FleetItemStatus.Waiting))
            item.Status = FleetItemStatus.NotStarted;
        fleet.Status = status;
        fleet.CompletedAt = now;
        await fleets.SaveAsync(ct);

        var stopped = fleet.Items.FirstOrDefault(item => item.Status is FleetItemStatus.Failed or FleetItemStatus.Refused);
        var counts = string.Join(", ", fleet.Items.GroupBy(item => item.Status).OrderBy(group => group.Key)
            .Select(group => group.Count() + " " + Name(group.Key).Replace('_', ' ')));
        var detail = "fleet " + fleet.Id + " " + Name(status)
            + (stopped is null ? "" : " at " + stopped.Product) + ": " + counts;
        try
        {
            // The fleet's end is no request of its own, so it is appended here, as its owner's.
            await audit.AppendAsync(new AuditRecord(fleet.Actor, "deployment.fleet.end", "portfolio",
                status == FleetRolloutStatus.Failed ? AuditOutcome.Failed : AuditOutcome.Succeeded, detail,
                stopped?.Detail is { } why ? why[..Math.Min(why.Length, 300)] : null), CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogAuditFailed(logger, exception.GetType().Name, fleet.Id);
        }
    }

    private static AppResult<FleetRolloutDto> Fail(AppError error) => AppResult<FleetRolloutDto>.Fail(error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fleet {Fleet}: submitted {Release} to {Target}.")]
    private static partial void LogSubmitted(ILogger logger, string release, string target, Guid fleet);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fleet {Fleet} waits: the deployment history could not be read.")]
    private static partial void LogHistoryFailed(ILogger logger, Guid fleet);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fleet {Fleet}: its end could not be audited ({FailureType}).")]
    private static partial void LogAuditFailed(ILogger logger, string failureType, Guid fleet);
}
