using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Application.Deployments.History.Services;

/// <summary>Measures a range of deployments the way the runner's statistics always have: outcome counts, the succeeded
/// share of finished rollouts, median rollout time and median recovery time per target.</summary>
public static class DeploymentMeasures
{
    /// <summary>Measures the entries of one range.</summary>
    /// <param name="entries">The range's deployments, in any order.</param>
    /// <param name="from">The range's start.</param>
    /// <param name="to">The range's end.</param>
    /// <returns>The measures.</returns>
    public static DeploymentMeasuresDto Measure(IEnumerable<DeploymentEntry> entries, DateTimeOffset from, DateTimeOffset to)
    {
        int succeeded = 0, failed = 0, refused = 0, pending = 0, deploys = 0;
        var rollouts = new List<double>();
        var recoveries = new List<double>();
        var failingSince = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var entry in entries.OrderBy(item => item.Record.SubmittedAt).ThenBy(item => item.Record.Id, StringComparer.Ordinal))
        {
            deploys++;
            var record = entry.Record;
            var began = record.StartedAt ?? record.SubmittedAt;
            var ended = record.CompletedAt;
            var target = record.TargetId ?? "unknown";
            switch (entry.Outcome)
            {
                case DeploymentOutcome.Succeeded:
                    succeeded++;
                    if (failingSince.Remove(target, out var since) && ended is { } recovered && recovered >= since)
                        recoveries.Add((recovered - since).TotalSeconds);
                    break;
                case DeploymentOutcome.Failed:
                    failed++;
                    failingSince.TryAdd(target, record.CompletedAt ?? record.SubmittedAt);
                    break;
                case DeploymentOutcome.Refused:
                    refused++;
                    break;
                default:
                    pending++;
                    break;
            }

            if (entry.Outcome is DeploymentOutcome.Succeeded or DeploymentOutcome.Failed && ended is { } end && end >= began)
                rollouts.Add((end - began).TotalSeconds);
        }

        var finished = succeeded + failed;
        return new DeploymentMeasuresDto(from, to, deploys, succeeded, failed, refused, pending,
            finished > 0 ? (double)succeeded / finished : null, Median(rollouts), Median(recoveries));
    }

    /// <summary>The middle value, or the mean of the two middle values; null for none.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The median.</returns>
    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return null;
        var ordered = values.Order().ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
    }
}
