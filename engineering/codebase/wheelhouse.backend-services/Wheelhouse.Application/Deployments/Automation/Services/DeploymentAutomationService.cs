using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.Automation.Constants;
using Wheelhouse.Application.Deployments.Automation.Models;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Services;
using Wheelhouse.Domain.Deployments.Entities;
using Wheelhouse.Domain.Deployments.Enums;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.Automation.Services;

/// <summary>Deploys on the owner's behalf. One pass looks at the targets inside their watch window after a successful
/// rollout, runs the bounded retention, watches each auto-deploying product's newest published release and queues a release newer than the
/// product's watermark for its dev targets, queues for prod a release that passed its dev watch of a product that
/// auto-promotes (each release once per prod target), then submits each queued deploy whose target runs no rollout
/// (one slot per target, latest wins). Every submission goes through the ordinary deploy command, so the gates, freezes
/// and the audit trail apply, with the actor <see cref="AutomationConstants.Actor"/>.</summary>
public sealed partial class DeploymentAutomationService(
    IProductsRepository products,
    ITargetsRepository targets,
    IServersRepository servers,
    IDeploymentAutomationStore store,
    Freezes.Services.DeployFreezeService freezes,
    IDeploymentGateway gateway,
    DeploymentHistoryService history,
    ISender sender,
    TimeProvider time,
    ILogger<DeploymentAutomationService> logger)
{
    /// <summary>Describes every product's automation choices and the queue.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The description.</returns>
    public async Task<DeploymentAutomationDto> DescribeAsync(CancellationToken ct)
    {
        var productRows = await products.GetAllAsync(ct);
        var settings = (await store.GetSettingsAsync(ct)).ToDictionary(row => row.ProductId);
        var targetRows = (await targets.GetAllAsync(ct)).ToDictionary(row => row.Id);
        var slugs = productRows.ToDictionary(row => row.Id, row => row.Slug);
        var queue = (await store.GetQueueAsync(ct))
            .Where(item => targetRows.ContainsKey(item.TargetId) && slugs.ContainsKey(targetRows[item.TargetId].ProductId))
            .Select(item =>
            {
                var target = targetRows[item.TargetId];
                return new QueuedDeploymentDto(target.Slug, slugs[target.ProductId], Environment(target), item.Release,
                    item.BundleId, item.PublishedAt, CauseName(item.Cause), item.QueuedAt);
            })
            .ToList();
        return new DeploymentAutomationDto(
            [.. productRows.OrderBy(row => row.Slug, StringComparer.Ordinal).Select(row => Describe(row, settings.GetValueOrDefault(row.Id)))],
            queue);
    }

    /// <summary>Describes one product's automation choices; a product without a row takes the defaults.</summary>
    /// <param name="product">The product.</param>
    /// <param name="row">Its recorded choices, if any.</param>
    /// <returns>The description.</returns>
    public static ProductAutomationDto Describe(ProductEntity product, ProductAutomationEntity? row)
    {
        var ci = AutomationConstants.CiDeployedProducts.Contains(product.Slug);
        return new ProductAutomationDto(product.Slug, !ci && (row?.AutoDeployDev ?? true), !ci && (row?.AutoPromote ?? false), ci);
    }

    /// <summary>Holds how recently a release must have passed its dev watch for auto-promote to take it, so turning
    /// auto-promote on never promotes an old release.</summary>
    public static readonly TimeSpan PromoteWithin = TimeSpan.FromHours(1);

    /// <summary>Runs one pass: look at watched rollouts, watch releases and queue new ones for dev, queue promotions,
    /// submit what may go.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="watchMinutes">How long a successful rollout is watched; zero skips the watch looks.</param>
    /// <returns>What the pass did.</returns>
    public async Task<DeploymentAutomationPass> RunOnceAsync(CancellationToken ct, int watchMinutes = 10)
    {
        if (watchMinutes > 0
            && await gateway.ReadAsync("watch", watchMinutes.ToString(CultureInfo.InvariantCulture), ct) is AppResult<JsonElement>.Failure)
            LogWatchWindowFailed(logger);
        // Retention: the runner prunes this host's bundles daily and after each rollout, and each target daily, two a pass.
        if (await gateway.ReadAsync("retention", null, ct) is AppResult<JsonElement>.Failure)
            LogRetentionFailed(logger);
        var productRows = await products.GetAllAsync(ct);
        var settings = (await store.GetSettingsAsync(ct)).ToDictionary(row => row.ProductId);
        var targetRows = await targets.GetAllAsync(ct);
        var queued = await WatchReleasesAsync(productRows, settings, targetRows, ct);
        var promoting = productRows.Any(product => Describe(product, settings.GetValueOrDefault(product.Id)).AutoPromote);
        if (!promoting && (await store.GetQueueAsync(ct)).Count == 0)
            return new DeploymentAutomationPass(queued, 0, 0);

        var loaded = await history.LoadAsync(ct);
        if (loaded is not AppResult<DeploymentHistory>.Success { Data: var deployments })
        {
            LogHistoryFailed(logger);
            return new DeploymentAutomationPass(queued, 0, 0);
        }

        if (promoting)
            queued += await QueuePromotionsAsync(productRows, settings, targetRows, deployments, ct);
        var (submitted, dropped) = await DrainAsync(productRows, settings, targetRows, deployments, ct);
        return new DeploymentAutomationPass(queued, submitted, dropped);
    }

    /// <summary>Queues for each prod target of a product that auto-promotes the newest release that passed its dev watch
    /// within <see cref="PromoteWithin"/> — and its end-to-end check, when it declares one — and that prod neither runs
    /// nor tried; each release is tried once per target. A
    /// frozen target and a prod on the local server, which needs its ID typed, are left to the owner.</summary>
    private async Task<int> QueuePromotionsAsync(
        IReadOnlyList<ProductEntity> productRows, IReadOnlyDictionary<Guid, ProductAutomationEntity> settings,
        IReadOnlyList<TargetEntity> targetRows, DeploymentHistory deployments, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var marks = (await store.GetPromotionsAsync(ct)).ToDictionary(row => row.TargetId);
        var queue = (await store.GetQueueAsync(ct)).ToDictionary(row => row.TargetId);
        var local = (await servers.GetAllAsync(ct))
            .Where(server => server.Provider == Wheelhouse.Domain.Servers.Enums.VpsProvider.Local)
            .Select(server => server.Id).ToHashSet();
        var count = 0;
        foreach (var product in productRows.Where(row => Describe(row, settings.GetValueOrDefault(row.Id)).AutoPromote))
        {
            var dev = targetRows.Where(row => row.ProductId == product.Id && row.Environment == DeploymentEnvironment.Dev)
                .Select(row => row.Slug).ToHashSet(StringComparer.Ordinal);
            var proven = deployments.Entries
                .Where(entry => entry.Record.TargetId is { } slug && dev.Contains(slug) && entry.Outcome == DeploymentOutcome.Succeeded
                    && entry.Record.Kind is "release" or null && entry.Record.Release is { } label && !label.StartsWith("sha-", StringComparison.Ordinal)
                    && entry.Record.BundleId is not null
                    && entry.Record.Watch is { Status: "passed", CompletedAt: { } passed } && now - passed <= PromoteWithin
                    && DeploymentVerificationDto.Clears(entry.Record.Verification, entry.Record.Verifies))
                .OrderByDescending(entry => entry.Record.PublishedAt ?? entry.Record.CompletedAt ?? entry.Record.SubmittedAt)
                .FirstOrDefault();
            if (proven is null)
                continue;
            foreach (var prod in targetRows.Where(row => row.ProductId == product.Id && row.Environment == DeploymentEnvironment.Prod))
            {
                if (local.Contains(prod.ServerId) || marks.GetValueOrDefault(prod.Id)?.Release == proven.Record.Release
                    || await freezes.CoveringAsync(product.Id, DeploymentEnvironment.Prod, ct) is not null)
                    continue;
                var onProd = deployments.Entries.Where(entry => entry.Record.TargetId == prod.Slug).ToList();
                var runs = onProd.FirstOrDefault(entry => entry.Outcome == DeploymentOutcome.Succeeded);
                if (onProd.Any(entry => entry.Record.Release == proven.Record.Release)
                    || (runs?.Record.PublishedAt is { } running && proven.Record.PublishedAt is { } offered && running >= offered))
                    continue;
                if (queue.GetValueOrDefault(prod.Id) is { } waiting && waiting.PublishedAt is { } held
                    && proven.Record.PublishedAt is { } candidate && held >= candidate)
                    continue;
                await store.SetQueuedAsync(new QueuedDeploymentEntity
                {
                    TargetId = prod.Id, BundleId = proven.Record.BundleId!, Release = proven.Record.Release!,
                    PublishedAt = proven.Record.PublishedAt, Cause = DeploymentCause.AutoPromote, QueuedAt = now,
                }, ct);
                await store.SetPromotionAsync(new AutoPromotionEntity
                {
                    TargetId = prod.Id, Release = proven.Record.Release!, AttemptedAt = now,
                }, ct);
                count++;
                LogQueued(logger, proven.Record.Release!, prod.Slug);
            }
        }

        return count;
    }

    /// <summary>Reads the newest release of every product that auto-deploys to dev and queues one newer than its
    /// watermark for each of its dev targets. A product read for the first time only records its watermark.</summary>
    private async Task<int> WatchReleasesAsync(
        IReadOnlyList<ProductEntity> productRows, IReadOnlyDictionary<Guid, ProductAutomationEntity> settings,
        IReadOnlyList<TargetEntity> targetRows, CancellationToken ct)
    {
        var watched = productRows
            .Where(product => product.Release is not null && Describe(product, settings.GetValueOrDefault(product.Id)).AutoDeployDev
                && targetRows.Any(target => target.ProductId == product.Id && target.Environment == DeploymentEnvironment.Dev))
            .ToList();
        if (watched.Count == 0)
            return 0;

        var read = await gateway.ReadAsync("latest", null, ct);
        if (read is not AppResult<JsonElement>.Success { Data: var document } || document.ValueKind != JsonValueKind.Array)
        {
            LogWatchFailed(logger);
            return 0;
        }

        var latest = new Dictionary<string, LatestRelease?>(StringComparer.Ordinal);
        foreach (var item in document.EnumerateArray())
            if (Text(item, "product") is { } slug && item.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                latest[slug] = item.TryGetProperty("release", out var release) && release.ValueKind == JsonValueKind.Object
                    && Text(release, "id") is { } id && Text(release, "release") is { } label
                    ? new LatestRelease(slug, id, label, Instant(release, "publishedAt"))
                    : null;

        var marks = (await store.GetWatermarksAsync(ct)).ToDictionary(row => row.ProductId);
        var now = time.GetUtcNow();
        var queue = (await store.GetQueueAsync(ct)).ToDictionary(row => row.TargetId);
        var count = 0;
        foreach (var product in watched)
        {
            if (!latest.TryGetValue(product.Slug, out var newest))
                continue; // GitHub could not serve it this time; the next pass reads it again.
            var mark = marks.GetValueOrDefault(product.Id);
            await store.SetWatermarkAsync(new ReleaseWatermarkEntity
            {
                ProductId = product.Id,
                Release = newest?.Release ?? mark?.Release,
                PublishedAt = newest?.PublishedAt ?? mark?.PublishedAt,
                CheckedAt = now,
            }, ct);
            if (mark is null || newest is null || !IsNewer(newest, mark))
                continue;

            foreach (var target in targetRows.Where(row => row.ProductId == product.Id && row.Environment == DeploymentEnvironment.Dev))
            {
                // Latest wins: a queued release is replaced only by a newer one.
                if (queue.GetValueOrDefault(target.Id) is { } waiting && waiting.PublishedAt is { } held
                    && newest.PublishedAt is { } offered && held >= offered)
                    continue;
                var item = new QueuedDeploymentEntity
                {
                    TargetId = target.Id, BundleId = newest.BundleId, Release = newest.Release, PublishedAt = newest.PublishedAt,
                    Cause = DeploymentCause.AutoDev, QueuedAt = now,
                };
                await store.SetQueuedAsync(item, ct);
                queue[target.Id] = item;
                count++;
                LogQueued(logger, newest.Release, target.Slug);
            }
        }

        return count;
    }

    /// <summary>Submits each queued deploy whose target runs no rollout, and drops one that no longer applies.</summary>
    private async Task<(int Submitted, int Dropped)> DrainAsync(
        IReadOnlyList<ProductEntity> productRows, IReadOnlyDictionary<Guid, ProductAutomationEntity> settings,
        IReadOnlyList<TargetEntity> targetRows, DeploymentHistory deployments, CancellationToken ct)
    {
        var queue = await store.GetQueueAsync(ct);
        if (queue.Count == 0)
            return (0, 0);
        var byId = targetRows.ToDictionary(row => row.Id);
        var productsById = productRows.ToDictionary(row => row.Id);
        int submitted = 0, dropped = 0;
        foreach (var item in queue)
        {
            if (!byId.TryGetValue(item.TargetId, out var target) || !productsById.TryGetValue(target.ProductId, out var product))
                continue; // the slot goes with its target
            var onTarget = deployments.Entries.Where(entry => entry.Record.TargetId == target.Slug).ToList();
            // A running rollout finishes first; the queued deploy waits for the next pass.
            if (onTarget.Any(entry => entry.Outcome == DeploymentOutcome.Pending))
                continue;

            var choices = Describe(product, settings.GetValueOrDefault(product.Id));
            var wanted = item.Cause == DeploymentCause.AutoPromote ? choices.AutoPromote : choices.AutoDeployDev;
            var done = onTarget.Any(entry => entry.Record.Release == item.Release
                && entry.Outcome is DeploymentOutcome.Succeeded or DeploymentOutcome.Failed);
            if (wanted && !done)
            {
                var result = await sender.SendAsync(new DeploymentStartCommand(target.Slug, item.BundleId,
                    AutomationConstants.Actor, Cause: item.Cause), ct);
                if (result is AppResult<JsonElement>.Success)
                {
                    submitted++;
                    LogSubmitted(logger, item.Release, target.Slug);
                }
                else
                    dropped++; // the audit trail records why it was refused
            }
            else
                dropped++;

            await store.RemoveQueuedAsync(item.TargetId, ct);
        }

        return (submitted, dropped);
    }

    /// <summary>Whether a release is newer than the watermark: published later, or the first one at all.</summary>
    private static bool IsNewer(LatestRelease newest, ReleaseWatermarkEntity mark) =>
        !string.Equals(newest.Release, mark.Release, StringComparison.Ordinal)
        && (mark.PublishedAt is not { } seen || newest.PublishedAt is not { } offered || offered > seen);

    private static string Environment(TargetEntity target) => target.Environment.ToString().ToLowerInvariant();

    private static string CauseName(DeploymentCause cause) =>
        cause == DeploymentCause.AutoPromote ? "auto_promote" : "auto_dev";

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Instant(JsonElement element, string property) =>
        Text(element, property) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var instant) ? instant : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The release watcher could not read the products' releases; the next pass retries.")]
    private static partial void LogWatchFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The retention pass could not run; the next pass retries.")]
    private static partial void LogRetentionFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The watch looks could not run; the next pass retries.")]
    private static partial void LogWatchWindowFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Queued automatic deploys wait: the deployment history could not be read.")]
    private static partial void LogHistoryFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued {Release} for {Target}.")]
    private static partial void LogQueued(ILogger logger, string release, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Submitted {Release} to {Target} automatically.")]
    private static partial void LogSubmitted(ILogger logger, string release, string target);
}
