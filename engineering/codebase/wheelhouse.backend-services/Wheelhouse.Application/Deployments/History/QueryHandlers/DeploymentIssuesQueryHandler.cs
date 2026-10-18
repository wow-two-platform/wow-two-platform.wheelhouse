using Wheelhouse.Application.Deployments.History.Models;
using Wheelhouse.Application.Deployments.History.Queries;
using Wheelhouse.Application.Deployments.History.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.History.QueryHandlers;

/// <summary>Handles <see cref="DeploymentIssuesQuery"/>. Each inventory target is judged by its latest rollout that
/// changed or tried to change it — refusals changed nothing and unknown receipts prove nothing, so both are passed over:
/// a failure there has had no success since, and a live one that started over <see cref="StuckAfter"/> ago is stuck.
/// A removed target is left out, since nothing can be redeployed to it.</summary>
public sealed class DeploymentIssuesQueryHandler(DeploymentHistoryService history, TimeProvider time)
    : IQueryHandler<DeploymentIssuesQuery, AppResult<IReadOnlyList<DeploymentIssueDto>>>
{
    /// <summary>Holds how long a rollout may stay live before it counts as stuck; a healthy one takes minutes.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(30);

    /// <summary>Holds the failures the runner refuses to deploy over until the target is reconciled, once they replaced
    /// containers (<c>runner.py</c>'s <c>apply</c> and <c>state</c>).</summary>
    private static readonly HashSet<string> LockingFailures = ["failed", "rollback_failed"];

    /// <inheritdoc />
    public async ValueTask<AppResult<IReadOnlyList<DeploymentIssueDto>>> HandleAsync(
        DeploymentIssuesQuery request, CancellationToken cancellationToken)
    {
        var read = await history.LoadAsync(cancellationToken);
        if (read is not AppResult<DeploymentHistory>.Success { Data: var loaded })
            return AppResult<IReadOnlyList<DeploymentIssueDto>>.Fail(((AppResult<DeploymentHistory>.Failure)read).Error);

        var now = time.GetUtcNow();
        var targets = loaded.Targets.Select(target => target.Slug).ToHashSet(StringComparer.Ordinal);
        var issues = new List<DeploymentIssueDto>();
        // Entries are newest first, so a target's first decisive entry is its latest.
        var judged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in loaded.Entries)
        {
            if (entry.Record.TargetId is not { } target || !targets.Contains(target)
                || entry.Outcome is not (DeploymentOutcome.Succeeded or DeploymentOutcome.Failed or DeploymentOutcome.Pending)
                || !judged.Add(target))
                continue;
            if (Judge(entry, now) is { } issue && DeploymentQuerying.MatchesEntities(entry, request.Filter))
                issues.Add(issue);
        }

        return AppResult<IReadOnlyList<DeploymentIssueDto>>.Ok([
            .. issues.OrderByDescending(issue => issue.NeedsReconcile).ThenByDescending(issue => issue.At),
        ]);
    }

    /// <summary>The issue a target's latest decisive rollout raises, or null when it succeeded or runs on time.</summary>
    /// <param name="entry">The target's latest rollout that succeeded, failed or is live.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The issue, or null.</returns>
    public static DeploymentIssueDto? Judge(DeploymentEntry entry, DateTimeOffset now)
    {
        var record = entry.Record;
        if (entry.Outcome == DeploymentOutcome.Pending)
        {
            var began = record.StartedAt ?? record.SubmittedAt;
            return now - began > StuckAfter
                ? new DeploymentIssueDto(DeploymentIssueKind.Stuck, true, began, DeploymentItemDto.From(entry))
                : null;
        }

        if (entry.Outcome == DeploymentOutcome.Succeeded)
            return record.Watch is { Status: "degraded" } watch
                ? new DeploymentIssueDto(DeploymentIssueKind.Degraded, false, watch.DegradedAt ?? record.CompletedAt ?? record.SubmittedAt,
                    DeploymentItemDto.From(entry))
                : null;
        if (entry.Outcome != DeploymentOutcome.Failed)
            return null;
        var kind = record.Status switch
        {
            "rolled_back" => DeploymentIssueKind.RolledBack,
            "rollback_failed" => DeploymentIssueKind.RollbackFailed,
            "interrupted" => DeploymentIssueKind.Interrupted,
            _ => DeploymentIssueKind.Failed,
        };
        var locked = record.MutationStarted == true && LockingFailures.Contains(record.Status);
        return new DeploymentIssueDto(kind, locked, record.CompletedAt ?? record.StartedAt ?? record.SubmittedAt,
            DeploymentItemDto.From(entry));
    }
}
