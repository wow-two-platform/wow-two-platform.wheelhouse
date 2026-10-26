using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Services;
using Wheelhouse.Application.Portfolio.Models;
using Wheelhouse.Application.Portfolio.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Portfolio.QueryHandlers;

/// <summary>Handles <see cref="PortfolioSummaryQuery"/> over the joined deployment history. Weeks start on Monday in the
/// owner's time zone and count finished rollouts only, as the activity calendar does: a deploy that changed nothing or
/// a live one counts in no week. The latest rollout skips deploys that changed nothing on the target; a rollout whose
/// product the inventory no longer holds counts nowhere.</summary>
public sealed class PortfolioSummaryQueryHandler(DeploymentHistoryService history, TimeProvider time)
    : IQueryHandler<PortfolioSummaryQuery, AppResult<PortfolioSummaryDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<PortfolioSummaryDto>> HandleAsync(PortfolioSummaryQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<PortfolioSummaryDto>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        var to = request.To ?? time.GetUtcNow();
        var weeks = WeeksEnding(LocalDate(to, zone), request.Weeks);
        // The summary reads as of `to`: a later submission is neither latest nor counted.
        var known = loaded.Entries.Where(entry => entry.Record.SubmittedAt <= to).ToList();

        // Which products and environments to report: every target, and every rollout of a product the inventory holds.
        var workspaces = new Dictionary<string, string?>(StringComparer.Ordinal);
        var environments = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var target in loaded.Targets)
            Note(target.Product, target.Workspace, target.Environment);
        foreach (var entry in known)
            if (entry is { Product: { } product, Workspace: { } workspace })
                Note(product, workspace, entry.Environment);

        var byWorkspace = workspaces.Values.OfType<string>().Distinct(StringComparer.Ordinal)
            .ToDictionary(slug => slug, _ => new (int Succeeded, int Failed)[weeks.Length], StringComparer.Ordinal);
        var products = new List<PortfolioProductDto>(environments.Count);
        foreach (var (product, names) in environments.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var own = known.Where(entry => entry.Product == product).ToList();
            var (succeeded, failed) = (0, 0);
            var rows = new List<PortfolioEnvironmentDto>(names.Count);
            foreach (var environment in names)
            {
                var entries = own.Where(entry => entry.Environment == environment).ToList();
                var weekly = new int[weeks.Length];
                foreach (var entry in entries)
                {
                    if (entry.NotApplied || entry.Outcome is not (DeploymentOutcome.Succeeded or DeploymentOutcome.Failed)
                        || WeekOf(entry.Record.SubmittedAt, zone, weeks) is not { } week)
                        continue;
                    weekly[week]++;
                    if (entry.Outcome == DeploymentOutcome.Succeeded) succeeded++;
                    else failed++;
                    if (workspaces[product] is { } workspace)
                    {
                        var counts = byWorkspace[workspace];
                        counts[week] = entry.Outcome == DeploymentOutcome.Succeeded
                            ? (counts[week].Succeeded + 1, counts[week].Failed)
                            : (counts[week].Succeeded, counts[week].Failed + 1);
                    }
                }

                // Entries arrive newest first. A deploy that changed nothing never stands for the environment; one that
                // needs a look marks the pill while nothing has changed the environment since.
                var latest = entries.FirstOrDefault(entry => !entry.NotApplied);
                var attempt = entries.TakeWhile(entry => !ReferenceEquals(entry, latest))
                    .FirstOrDefault(entry => entry.NotApplied && entry.State.Attention == DeploymentAttention.Look);
                var slug = loaded.Targets.FirstOrDefault(target => target.Product == product && target.Environment == environment)?.Slug;
                rows.Add(new PortfolioEnvironmentDto(
                    environment,
                    Map(latest),
                    Map(entries.FirstOrDefault(entry => entry.Outcome == DeploymentOutcome.Succeeded && !entry.NotApplied)),
                    weekly,
                    Map(attempt),
                    slug is null ? null : loaded.Stops.GetValueOrDefault(slug)));
            }

            products.Add(new PortfolioProductDto(product, workspaces[product], succeeded, failed, rows));
        }

        return AppResult<PortfolioSummaryDto>.Ok(new PortfolioSummaryDto(
            request.TimeZone, weeks, products,
            [
                .. byWorkspace.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new PortfolioWorkspaceDto(
                    pair.Key, pair.Value.Sum(week => week.Succeeded), pair.Value.Sum(week => week.Failed),
                    [.. pair.Value.Select(week => new PortfolioWeekDto(week.Succeeded, week.Failed))])),
            ]));

        void Note(string product, string? workspace, string? environment)
        {
            if (!workspaces.TryGetValue(product, out var noted) || noted is null)
                workspaces[product] = workspace;
            if (!environments.TryGetValue(product, out var set))
                environments[product] = set = new SortedSet<string>(EnvironmentOrder.Instance);
            if (environment is not null)
                set.Add(environment);
        }
    }

    /// <summary>The Mondays of the <paramref name="count"/> weeks ending with the one <paramref name="today"/> falls in,
    /// oldest first. @internal</summary>
    private static DateOnly[] WeeksEnding(DateOnly today, int count)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return [.. Enumerable.Range(0, count).Select(index => monday.AddDays(-7 * (count - 1 - index)))];
    }

    /// <summary>The week an instant falls in, or null before the first. @internal</summary>
    private static int? WeekOf(DateTimeOffset instant, TimeZoneInfo zone, DateOnly[] weeks)
    {
        var days = LocalDate(instant, zone).DayNumber - weeks[0].DayNumber;
        return days < 0 || days / 7 >= weeks.Length ? null : days / 7;
    }

    /// <summary>The calendar date an instant falls on in a time zone. @internal</summary>
    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>A rollout as a pill's details show it; null without one. @internal</summary>
    private static PortfolioDeployDto? Map(DeploymentEntry? entry) =>
        entry is null
            ? null
            : new PortfolioDeployDto(entry.Record.Id, entry.Record.Release, entry.Record.Status, entry.Outcome,
                entry.Record.Actor, entry.Record.SubmittedAt, entry.Record.CompletedAt, entry.Record.Reason ?? entry.State.Cause?.Message,
                entry.State.State, entry.State.Substate, entry.State.Attention,
                entry.State.Services?.Count(item => item.Result is "updated" or "new" or "unchanged" or "affected"),
                entry.State.Services?.Count, entry.Record.Watch?.Status);

    /// <summary>Orders environments in promotion order — dev, then prod — and any other after them by name. @internal</summary>
    private sealed class EnvironmentOrder : IComparer<string>
    {
        /// <summary>Gets the one comparer.</summary>
        public static EnvironmentOrder Instance { get; } = new();

        /// <inheritdoc />
        public int Compare(string? x, string? y) =>
            Rank(x).CompareTo(Rank(y)) is var byRank and not 0 ? byRank : string.CompareOrdinal(x, y);

        /// <summary>An environment's place. @internal</summary>
        private static int Rank(string? environment) => environment switch { "dev" => 0, "prod" => 1, _ => 2 };
    }
}
