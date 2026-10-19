using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Application.Deployments.History.Services;

/// <summary>Pure rules that pick deployments out of the history: entities, states and time.</summary>
public static class DeploymentQuerying
{
    /// <summary>Whether an entry belongs to any of the filter's entities: a listed workspace's or product's deployment,
    /// or one that changed a listed service. A bundle the runner no longer holds counts as changing every service of its
    /// product, since its rollout replaced them all.</summary>
    /// <param name="entry">The deployment.</param>
    /// <param name="filter">The filter.</param>
    /// <returns><c>true</c> when it matches, or when the filter names no entity.</returns>
    public static bool MatchesEntities(DeploymentEntry entry, DeploymentFilter filter)
    {
        if (!filter.HasEntities)
            return true;
        if (entry.Workspace is { } workspace && filter.Workspaces.Contains(workspace, StringComparer.Ordinal))
            return true;
        if (entry.Product is not { } product)
            return false;
        if (filter.Products.Contains(product, StringComparer.Ordinal))
            return true;
        foreach (var service in filter.Services)
        {
            var separator = service.IndexOf('/', StringComparison.Ordinal);
            if (separator <= 0 || !service.AsSpan(0, separator).SequenceEqual(product))
                continue;
            var key = service[(separator + 1)..];
            if (entry.Changes is null || entry.Changes.Any(change => change.Changed && change.Service == key))
                return true;
        }

        return false;
    }

    /// <summary>Whether an entry is one the status filter shows. A deployment that changed nothing shows only when the
    /// filter includes them or asks for them alone; the old <c>failed</c> keeps its old meaning, a failed check
    /// included.</summary>
    /// <param name="entry">The deployment.</param>
    /// <param name="status">The status filter.</param>
    /// <param name="includeNotApplied">Whether deployments that changed nothing show beside the others.</param>
    /// <returns><c>true</c> when it shows.</returns>
    public static bool MatchesStatus(DeploymentEntry entry, DeploymentStatusFilter status, bool includeNotApplied = false) =>
        (includeNotApplied || status is DeploymentStatusFilter.NotApplied or DeploymentStatusFilter.Failed || !entry.NotApplied)
        && status switch
        {
            DeploymentStatusFilter.Succeeded => entry.State.State == DeploymentState.Succeeded,
            DeploymentStatusFilter.Failed => entry.Outcome == DeploymentOutcome.Failed,
            DeploymentStatusFilter.NeedsAction => entry.State.Attention == DeploymentAttention.Act,
            DeploymentStatusFilter.Look => entry.State.Attention == DeploymentAttention.Look,
            DeploymentStatusFilter.NotApplied => entry.NotApplied,
            _ => true,
        };

    /// <summary>Whether an entry was submitted inside a range: from inclusive, to exclusive; an open end is unbounded.</summary>
    /// <param name="entry">The deployment.</param>
    /// <param name="from">The range's start.</param>
    /// <param name="to">The range's end.</param>
    /// <returns><c>true</c> when it falls inside.</returns>
    public static bool InRange(DeploymentEntry entry, DateTimeOffset? from, DateTimeOffset? to) =>
        (from is not { } start || entry.Record.SubmittedAt >= start) && (to is not { } end || entry.Record.SubmittedAt < end);

    /// <summary>The applied deploys a newer applied deploy replaced on the same target and local day: the attempts a
    /// latest-of-day view hides. A deploy that changed nothing never replaces one, nor counts here.</summary>
    /// <param name="entries">The deployments to compare, in any order.</param>
    /// <param name="zone">The time zone days are counted in.</param>
    /// <returns>The replaced deployments' IDs.</returns>
    public static HashSet<string> Superseded(IEnumerable<DeploymentEntry> entries, TimeZoneInfo zone)
    {
        var latest = new HashSet<(string Lane, DateOnly Day)>();
        var superseded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries
                     .Where(entry => !entry.NotApplied)
                     .OrderByDescending(entry => entry.Record.SubmittedAt)
                     .ThenByDescending(entry => entry.Record.Id, StringComparer.Ordinal))
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(entry.Record.SubmittedAt, zone).DateTime);
            if (!latest.Add((LaneOf(entry), day)))
                superseded.Add(entry.Record.Id);
        }

        return superseded;
    }

    /// <summary>The lane a deploy runs in: its target, else its product and environment for a record without one.</summary>
    /// <param name="entry">The deployment.</param>
    /// <returns>The lane's key.</returns>
    public static string LaneOf(DeploymentEntry entry) =>
        entry.Record.TargetId ?? $"{entry.Product ?? "unknown"}/{entry.Environment ?? "unknown"}";

    /// <summary>The entries a filter shows, in their order: those matching it, without the attempts a newer deploy
    /// replaced that day when it keeps each target's latest. Which deploy is latest follows the entities and the range,
    /// never the status, so a status narrows what the latest deploys are.</summary>
    /// <param name="entries">The history, newest first.</param>
    /// <param name="filter">The filter.</param>
    /// <returns>The matching entries.</returns>
    public static IEnumerable<DeploymentEntry> Select(IReadOnlyList<DeploymentEntry> entries, DeploymentFilter filter)
    {
        var hidden = filter.LatestOfDay
            ? Superseded(entries.Where(entry => MatchesEntities(entry, filter) && InRange(entry, filter.From, filter.To)),
                TimeZoneInfo.FindSystemTimeZoneById(filter.TimeZone))
            : null;
        return entries.Where(entry => Matches(entry, filter) && hidden?.Contains(entry.Record.Id) != true);
    }

    /// <summary>Whether an entry matches the whole filter: entities, outcomes and range.</summary>
    /// <param name="entry">The deployment.</param>
    /// <param name="filter">The filter.</param>
    /// <returns><c>true</c> when it matches.</returns>
    public static bool Matches(DeploymentEntry entry, DeploymentFilter filter) =>
        MatchesEntities(entry, filter) && MatchesStatus(entry, filter.Status, filter.IncludeNotApplied)
        && InRange(entry, filter.From, filter.To);
}
