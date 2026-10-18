using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentActivityQuery"/>: finished rollouts only, so a refused or live submission never
/// colours a day. The status filter applies, so a failures-only view counts no success. The same rollouts name the
/// range's busiest products; a rollout whose product is unknown counts on its day only.</summary>
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
        var products = new Dictionary<string, (int Succeeded, int Failed)>(StringComparer.Ordinal);
        foreach (var entry in loaded.Entries.Where(entry => DeploymentQuerying.Matches(entry, filter)))
        {
            if (entry.Outcome is not (DeploymentOutcome.Succeeded or DeploymentOutcome.Failed))
                continue;
            var day = LocalDate(entry.Record.SubmittedAt, zone);
            var counts = days.GetValueOrDefault(day);
            days[day] = Count(counts, entry.Outcome);
            if (entry.Product is { } product)
                products[product] = Count(products.GetValueOrDefault(product), entry.Outcome);
        }

        return AppResult<DeploymentActivityDto>.Ok(new DeploymentActivityDto(
            LocalDate(from, zone), LocalDate(to.AddTicks(-1), zone), request.TimeZone,
            [.. days.Select(pair => new DeploymentDayDto(pair.Key, pair.Value.Succeeded, pair.Value.Failed))],
            [
                .. products
                    .OrderByDescending(pair => pair.Value.Succeeded + pair.Value.Failed)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(DeploymentActivityDto.BusiestLimit)
                    .Select(pair => new DeploymentProductCountDto(pair.Key, pair.Value.Succeeded, pair.Value.Failed)),
            ]));
    }

    /// <summary>Adds one finished rollout to a tally. @internal</summary>
    private static (int Succeeded, int Failed) Count((int Succeeded, int Failed) counts, string outcome) =>
        outcome == DeploymentOutcome.Succeeded ? (counts.Succeeded + 1, counts.Failed) : (counts.Succeeded, counts.Failed + 1);

    /// <summary>The calendar date an instant falls on in a time zone. @internal</summary>
    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
