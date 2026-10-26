using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Domain.Deployments.Entities;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.Services;

/// <summary>Joins the runner's deployment records with the inventory: each target's product, workspace and environment,
/// the target's previous success and the services each rollout changed. The deploys that never reached their target —
/// refused, replaced in the queue, cancelled, expired or aborted — join from the control plane's intents, so the
/// history lists them as Not applied.</summary>
public sealed class DeploymentHistoryService(
    IDeploymentRecords records,
    ITargetsRepository targets,
    IProductsRepository products,
    IWorkspacesRepository workspaces,
    IDeploymentAutomationStore intents,
    IDeploymentRenders? renders = null)
{
    /// <summary>Reads every recorded deployment with the inventory joined.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The history, or the runner's failure.</returns>
    public async Task<AppResult<DeploymentHistory>> LoadAsync(CancellationToken ct)
    {
        var read = await records.ListAsync(ct);
        if (read is not AppResult<IReadOnlyList<DeploymentRecord>>.Success { Data: var list })
            return AppResult<DeploymentHistory>.Fail(((AppResult<IReadOnlyList<DeploymentRecord>>.Failure)read).Error);

        var workspaceSlugs = (await workspaces.GetAllAsync(ct)).ToDictionary(row => row.Id, row => row.Slug);
        var productRows = await products.GetAllAsync(ct);
        var productSlugs = productRows.ToDictionary(row => row.Id, row => row.Slug);
        var productModes = productRows.ToDictionary(row => row.Id, row => row.Deploy);
        var productWorkspaces = productRows.ToDictionary(
            row => row.Slug, row => workspaceSlugs.GetValueOrDefault(row.WorkspaceId), StringComparer.Ordinal);
        var facts = (await targets.GetAllAsync(ct))
            .Where(row => productSlugs.ContainsKey(row.ProductId))
            .Select(row => new DeploymentTargetFacts(
                row.Slug, productSlugs[row.ProductId], productWorkspaces.GetValueOrDefault(productSlugs[row.ProductId]),
                row.Environment.ToString().ToLowerInvariant(), productModes[row.ProductId]))
            .ToArray();
        var byTarget = facts.ToDictionary(fact => fact.Slug, StringComparer.Ordinal);
        var unapplied = (await intents.GetIntentsAsync(ct)).Select(FromIntent).ToList();
        var settings = renders is null ? new Dictionary<string, string>() : await renders.GetChangesAsync(ct);

        // Oldest first, so each submission meets its target's previous success before it.
        var lastSuccess = new Dictionary<string, DeploymentRecord>(StringComparer.Ordinal);
        var entries = new List<DeploymentEntry>(list.Count + unapplied.Count);
        var controls = list.Where(item => item.Operation is not null).ToList();
        foreach (var record in list.Where(item => item.Operation is null).Concat(unapplied)
                     .OrderBy(item => item.SubmittedAt).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var target = record.TargetId is { } id ? byTarget.GetValueOrDefault(id) : null;
            var product = target?.Product ?? record.Product;
            var workspace = target?.Workspace ?? (product is null ? null : productWorkspaces.GetValueOrDefault(product));
            var previous = record.TargetId is { } key ? lastSuccess.GetValueOrDefault(key) : null;
            entries.Add(new DeploymentEntry(record, product, workspace, target?.Environment ?? record.Environment, previous,
                Compare(record, previous), settings.TryGetValue(record.Id, out var changes) ? Changes(changes) : null));
            if (record.TargetId is { } slug && DeploymentOutcome.Of(record.Status) == DeploymentOutcome.Succeeded)
                lastSuccess[slug] = record;
        }

        entries.Reverse();
        var controlEntries = controls
            .OrderByDescending(item => item.SubmittedAt).ThenByDescending(item => item.Id, StringComparer.Ordinal)
            .Select(record =>
            {
                var target = record.TargetId is { } id ? byTarget.GetValueOrDefault(id) : null;
                var product = target?.Product ?? record.Product;
                return new DeploymentEntry(record, product,
                    target?.Workspace ?? (product is null ? null : productWorkspaces.GetValueOrDefault(product)),
                    target?.Environment ?? record.Environment, null, null);
            })
            .ToList();
        return AppResult<DeploymentHistory>.Ok(new DeploymentHistory(entries, facts)
        {
            Controls = controlEntries,
            Stops = StopsOf(controlEntries),
        });
    }

    /// <summary>What the owner keeps stopped on each target: what its newest control that ran to an end (succeeded or
    /// failed) left stopped. A control the runner refused, or one cut short and reconciled, says nothing.</summary>
    /// <param name="controls">The controls, newest first.</param>
    /// <returns>Each target with something stopped.</returns>
    public static IReadOnlyDictionary<string, TargetStopDto> StopsOf(IEnumerable<DeploymentEntry> controls)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stops = new Dictionary<string, TargetStopDto>(StringComparer.Ordinal);
        foreach (var entry in controls)
        {
            var record = entry.Record;
            if (record.TargetId is not { } target || record.CompletedAt is null || record.Status is not ("succeeded" or "failed")
                || !seen.Add(target))
                continue;
            if (record.Stopped is { } stopped && (stopped.Environment || stopped.Services.Count > 0))
                stops[target] = stopped;
        }

        return stops;
    }

    /// <summary>Reads a render's recorded changes; an unreadable record reads as none.</summary>
    /// <param name="json">The changes as recorded.</param>
    /// <returns>The changes, or null.</returns>
    public static IReadOnlyList<Settings.Models.SettingChangeDto>? Changes(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Settings.Models.SettingChangeDto>>(json,
                System.Text.Json.JsonSerializerOptions.Web);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>A deploy that never reached its target, as a record beside the runner's: it ended when it was dropped,
    /// and its status stays the runner's word for a refusal, so integrations that read the status see it changed
    /// nothing.</summary>
    /// <param name="intent">The control plane's record.</param>
    /// <returns>The record, carrying the intent.</returns>
    public static DeploymentRecord FromIntent(DeploymentIntentEntity intent) => new()
    {
        Id = intent.Id.ToString(),
        TargetId = intent.Target,
        Environment = intent.Environment,
        BundleId = intent.BundleId,
        Release = intent.Release,
        Product = intent.Product,
        Actor = intent.Actor,
        FleetId = intent.FleetId?.ToString(),
        Status = "rejected",
        Reason = intent.Message,
        SubmittedAt = intent.At,
        CompletedAt = intent.At,
        Intent = intent,
    };

    /// <summary>Compares each service's image with the target's previous success; every service is new without one.</summary>
    /// <param name="record">The rollout.</param>
    /// <param name="previous">The target's previous success, if any.</param>
    /// <returns>Each service, the rollout's first and then any the release dropped; null when the bundle is unknown.</returns>
    public static IReadOnlyList<ServiceChangeDto>? Compare(DeploymentRecord record, DeploymentRecord? previous)
    {
        if (record.Services is null)
            return null;
        var before = previous?.Services;
        var changes = record.Services
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var prior = before?.GetValueOrDefault(pair.Key);
                return new ServiceChangeDto(pair.Key, prior is null ? null : Label(prior), Label(pair.Value),
                    prior is null || !string.Equals(prior.Image, pair.Value.Image, StringComparison.Ordinal));
            })
            .ToList();
        if (before is not null)
            changes.AddRange(before
                .Where(pair => !record.Services.ContainsKey(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ServiceChangeDto(pair.Key, Label(pair.Value), null, true)));
        return changes;
    }

    /// <summary>Names a service's build: its declared version, else its image digest's first 12 hex characters.</summary>
    /// <param name="service">The service in a bundle.</param>
    /// <returns>The label, or null when the bundle declares neither.</returns>
    public static string? Label(DeploymentServiceRecord service)
    {
        if (!string.IsNullOrEmpty(service.Version))
            return service.Version;
        if (service.Image is not { } image)
            return null;
        var digest = image.IndexOf("@sha256:", StringComparison.Ordinal);
        return digest < 0 ? image : image[(digest + 8)..Math.Min(image.Length, digest + 20)];
    }
}
