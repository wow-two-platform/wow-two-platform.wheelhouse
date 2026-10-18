import { DeploymentIssueKind, type DeploymentIssue } from './models/DeploymentIssue';

/** Pure rules of the Issues section: what each issue says and which actions it offers. */
export const DeploymentIssues = {
  /** What happened, in a few words: `Rolled back`, `Stuck on Verify services`. */
  headline(issue: Pick<DeploymentIssue, 'kind' | 'deployment'>): string {
    switch (issue.kind) {
      case DeploymentIssueKind.RolledBack:
        return 'Rolled back to the previous release';
      case DeploymentIssueKind.RollbackFailed:
        return 'Failed, and restoring the previous release failed too';
      case DeploymentIssueKind.Interrupted:
        return 'Stopped mid-way and reconciled; nothing deployed since';
      case DeploymentIssueKind.Degraded: {
        const finding = issue.deployment.watch?.findings[0];
        return finding ? `Regressed after the deploy: ${finding}` : 'Regressed after the deploy';
      }
      case DeploymentIssueKind.Stuck: {
        const step = issue.deployment.currentStep?.name;
        return step ? `Stuck on ${step}` : 'Stuck before its first step';
      }
      default:
        return 'Failed';
    }
  },

  /** Redeploying is offered unless the target is likely locked, where the runner would refuse it until a reconcile. */
  canRedeploy(issue: Pick<DeploymentIssue, 'needsReconcile' | 'deployment'>): boolean {
    return !issue.needsReconcile && Boolean(issue.deployment.targetId && issue.deployment.bundleId);
  },

  /** Reconciling is offered when the target is likely locked. */
  canReconcile(issue: Pick<DeploymentIssue, 'needsReconcile' | 'deployment'>): boolean {
    return issue.needsReconcile && Boolean(issue.deployment.targetId);
  },
} as const;
