import type { DeploymentItem } from './DeploymentItem';

/** What went wrong on a target: its latest rollout failed, rolled back, failed to roll back or was interrupted with no
 *  success since, it succeeded and then regressed while watched, or a rollout has been live too long. */
export const DeploymentIssueKind = {
  Failed: 'failed',
  RolledBack: 'rolled_back',
  RollbackFailed: 'rollback_failed',
  Interrupted: 'interrupted',
  Degraded: 'degraded',
  Stuck: 'stuck',
} as const;
export type DeploymentIssueKind = (typeof DeploymentIssueKind)[keyof typeof DeploymentIssueKind];

/** A target that needs attention, judged by its latest rollout. */
export interface DeploymentIssue {
  kind: DeploymentIssueKind;
  /** The target is likely locked until someone reconciles it; its own state decides. */
  needsReconcile: boolean;
  /** When it happened: the failure's completion, or when the stuck rollout started. */
  at: string;
  /** The rollout in question. */
  deployment: DeploymentItem;
}
