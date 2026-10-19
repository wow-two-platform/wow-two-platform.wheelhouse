using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentStatsQuery"/>: the filter's entities and range, not its status. The figures
/// follow the view: with <see cref="DeploymentFilter.LatestOfDay"/> they count each target's latest applied deploy of a
/// local day alone, as the records show it, and the deploys that changed nothing count only when the filter includes
/// them. <see cref="DeploymentMeasuresDto.NotApplied"/> and <see cref="DeploymentMeasuresDto.Superseded"/> always count
/// the deploys that changed nothing and the attempts a later deploy replaced that day, so the console can say how many a
/// latest-of-day view hides.</summary>
public sealed class DeploymentStatsQueryHandler(DeploymentHistoryService history, TimeProvider time)
    : IQueryHandler<DeploymentStatsQuery, AppResult<DeploymentStatsDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploymentStatsDto>> HandleAsync(DeploymentStatsQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<DeploymentStatsDto>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);

        var filter = request.Filter;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(filter.TimeZone);
        var to = filter.To ?? time.GetUtcNow();
        var from = filter.From ?? to.AddDays(-30);
        var before = from - (to - from);
        var entities = loaded.Entries.Where(entry => DeploymentQuerying.MatchesEntities(entry, filter)).ToList();
        var inRange = entities.Where(entry => DeploymentQuerying.InRange(entry, from, to)).ToList();
        var superseded = DeploymentQuerying.Superseded(inRange, zone);
        var earlier = Counted(entities.Where(entry => DeploymentQuerying.InRange(entry, before, from)).ToList(), filter, zone);
        var current = DeploymentMeasures.Measure(Counted(inRange, filter, zone, superseded), from, to) with
        {
            NotApplied = inRange.Count(entry => entry.NotApplied),
            Superseded = superseded.Count,
        };
        return AppResult<DeploymentStatsDto>.Ok(new DeploymentStatsDto(
            current, earlier.Count > 0 ? DeploymentMeasures.Measure(earlier, before, from) : null));
    }

    /// <summary>The deploys one range's figures count: without those that changed nothing unless the filter includes
    /// them, and without the attempts a later deploy replaced that day when it keeps each day's latest.</summary>
    /// <param name="entries">The range's deployments.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="zone">The time zone days are counted in.</param>
    /// <param name="superseded">The range's replaced attempts, when already known.</param>
    /// <returns>The counted deployments.</returns>
    private static List<DeploymentEntry> Counted(
        IReadOnlyList<DeploymentEntry> entries, DeploymentFilter filter, TimeZoneInfo zone, HashSet<string>? superseded = null)
    {
        var hidden = filter.LatestOfDay ? superseded ?? DeploymentQuerying.Superseded(entries, zone) : null;
        return [.. entries.Where(entry =>
            (filter.IncludeNotApplied || !entry.NotApplied) && hidden?.Contains(entry.Record.Id) != true)];
    }
}
