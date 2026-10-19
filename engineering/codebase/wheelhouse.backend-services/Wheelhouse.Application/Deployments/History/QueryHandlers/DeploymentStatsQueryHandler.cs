using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentStatsQuery"/>: the filter's entities and range, not its status nor its
/// latest-of-day choice, so every attempt counts; the deploys that changed nothing count in the figures only when the
/// filter includes them, while <see cref="DeploymentMeasuresDto.NotApplied"/> always counts them and
/// <see cref="DeploymentMeasuresDto.Superseded"/> the attempts a later deploy replaced that day, so the console can say
/// how many a latest-of-day view hides.</summary>
public sealed class DeploymentStatsQueryHandler(DeploymentHistoryService history, TimeProvider time)
    : IQueryHandler<DeploymentStatsQuery, AppResult<DeploymentStatsDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploymentStatsDto>> HandleAsync(DeploymentStatsQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<DeploymentStatsDto>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);

        var to = request.Filter.To ?? time.GetUtcNow();
        var from = request.Filter.From ?? to.AddDays(-30);
        var before = from - (to - from);
        var entities = loaded.Entries.Where(entry => DeploymentQuerying.MatchesEntities(entry, request.Filter)).ToList();
        var scoped = entities.Where(entry => request.Filter.IncludeNotApplied || !entry.NotApplied).ToList();
        var earlier = scoped.Where(entry => DeploymentQuerying.InRange(entry, before, from)).ToList();
        var current = DeploymentMeasures.Measure(scoped.Where(entry => DeploymentQuerying.InRange(entry, from, to)), from, to) with
        {
            NotApplied = entities.Count(entry => entry.NotApplied && DeploymentQuerying.InRange(entry, from, to)),
            Superseded = DeploymentQuerying.Superseded(
                entities.Where(entry => DeploymentQuerying.InRange(entry, from, to)),
                TimeZoneInfo.FindSystemTimeZoneById(request.Filter.TimeZone)).Count,
        };
        return AppResult<DeploymentStatsDto>.Ok(new DeploymentStatsDto(
            current, earlier.Count > 0 ? DeploymentMeasures.Measure(earlier, before, from) : null));
    }
}
