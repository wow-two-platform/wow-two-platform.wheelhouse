namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents a target that needs attention: its latest rollout failed with no success since, or a rollout on
/// it has been live too long.</summary>
/// <param name="Kind">What went wrong: one of <see cref="DeploymentIssueKind"/>'s names.</param>
/// <param name="NeedsReconcile">Whether the target is likely locked until someone reconciles it: a stuck rollout, or a
/// failure that had already replaced containers. Read only from local records; the target's own state decides.</param>
/// <param name="At">When it happened: the failure's completion, or when the stuck rollout started.</param>
/// <param name="Deployment">The rollout in question.</param>
public sealed record DeploymentIssueDto(string Kind, bool NeedsReconcile, DateTimeOffset At, DeploymentItemDto Deployment);
