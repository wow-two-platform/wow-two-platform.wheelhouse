using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentActivityQuery"/>: each day counts the finished rollouts that changed its
/// environment by the attention they need, so a live submission never colours a day, and, when the filter includes
/// them, the deploys that changed nothing beside them. The status filter applies, so a needs-action view counts no
/// success. The same applied rollouts name the range's busiest products; a rollout whose product is unknown counts on
/// its day only.</summary>
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
        var days = new SortedDictionary<DateOnly, DeploymentDayDto>();
        var products = new Dictionary<string, (int Succeeded, int Failed)>(StringComparer.Ordinal);
        foreach (var entry in loaded.Entries.Where(entry => DeploymentQuerying.Matches(entry, filter)))
        {
            var date = LocalDate(entry.Record.SubmittedAt, zone);
            var day = days.GetValueOrDefault(date) ?? new DeploymentDayDto(date, 0, 0);
            if (entry.NotApplied)
            {
                days[date] = day with { NotApplied = day.NotApplied + 1 };
                continue;
            }

            if (entry.State.State is DeploymentState.InProgress or DeploymentState.Unknown
                || entry.Outcome is not (DeploymentOutcome.Succeeded or DeploymentOutcome.Failed))
                continue;
            var succeeded = entry.Outcome == DeploymentOutcome.Succeeded;
            days[date] = day with
            {
                Succeeded = day.Succeeded + (succeeded ? 1 : 0),
                Failed = day.Failed + (succeeded ? 0 : 1),
                Calm = day.Calm + (entry.State.Attention == DeploymentAttention.None ? 1 : 0),
                Look = day.Look + (entry.State.Attention == DeploymentAttention.Look ? 1 : 0),
                Act = day.Act + (entry.State.Attention == DeploymentAttention.Act ? 1 : 0),
            };
            if (entry.Product is { } product)
            {
                var counts = products.GetValueOrDefault(product);
                products[product] = succeeded ? (counts.Succeeded + 1, counts.Failed) : (counts.Succeeded, counts.Failed + 1);
            }
        }

        return AppResult<DeploymentActivityDto>.Ok(new DeploymentActivityDto(
            LocalDate(from, zone), LocalDate(to.AddTicks(-1), zone), request.TimeZone,
            [.. days.Values],
            [
                .. products
                    .OrderByDescending(pair => pair.Value.Succeeded + pair.Value.Failed)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(DeploymentActivityDto.BusiestLimit)
                    .Select(pair => new DeploymentProductCountDto(pair.Key, pair.Value.Succeeded, pair.Value.Failed)),
            ]));
    }

    /// <summary>The calendar date an instant falls on in a time zone. @internal</summary>
    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
