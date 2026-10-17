using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentActivityQuery"/>: finished rollouts only, so a refused or live submission never
/// colours a day. The status filter applies, so a failures-only view counts no success.</summary>
public sealed class DeploymentActivityQueryHandler(DeploymentHistoryService history, TimeProvider time)
    : IQueryHandler<DeploymentActivityQuery, AppResult<DeploymentActivityDto>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<DeploymentActivityDto>> HandleAsync(DeploymentActivityQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<DeploymentActivityDto>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        var to = request.Filter.To ?? time.GetUtcNow();
        var from = request.Filter.From ?? to.AddMonths(-6);
        var filter = request.Filter with { From = from, To = to };
        var days = new SortedDictionary<DateOnly, (int Succeeded, int Failed)>();
        foreach (var entry in loaded.Entries.Where(entry => DeploymentQuerying.Matches(entry, filter)))
        {
            if (entry.Outcome is not (DeploymentOutcome.Succeeded or DeploymentOutcome.Failed))
                continue;
            var day = LocalDate(entry.Record.SubmittedAt, zone);
            var counts = days.GetValueOrDefault(day);
            days[day] = entry.Outcome == DeploymentOutcome.Succeeded
                ? (counts.Succeeded + 1, counts.Failed)
                : (counts.Succeeded, counts.Failed + 1);
        }

        return AppResult<DeploymentActivityDto>.Ok(new DeploymentActivityDto(
            LocalDate(from, zone), LocalDate(to.AddTicks(-1), zone), request.TimeZone,
            [.. days.Select(pair => new DeploymentDayDto(pair.Key, pair.Value.Succeeded, pair.Value.Failed))]));
    }

    /// <summary>The calendar date an instant falls on in a time zone. @internal</summary>
    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
