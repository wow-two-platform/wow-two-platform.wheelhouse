import { JobStatus } from './enums/JobStatus';
import { TargetCondition } from './enums/TargetCondition';
import type { ReleaseArtifact } from './models/ReleaseArtifact';

/** How a target's release compares with the newest published one for its product. */
export interface ReleaseDrift {
  latest: ReleaseArtifact | null;
  /** Releases published after the running one; null when the running release is not in the catalog. */
  behind: number | null;
}

/** The badge palette a status renders with. */
export type StatusVariant = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

/** The palette of a status dot; the same colours as {@link StatusVariant}, named as the indicator names them. */
export type IndicatorTone = 'neutral' | 'info' | 'success' | 'warning' | 'destructive';

const INDICATOR: Record<StatusVariant, IndicatorTone> = {
  neutral: 'neutral', info: 'info', success: 'success', warning: 'warning', danger: 'destructive',
};

const PENDING: ReadonlySet<JobStatus> = new Set([JobStatus.Submitting, JobStatus.Queued, JobStatus.Running]);

/** Pure rules over deployment statuses and target conditions. */
export const DeploymentExtensions = {
  /** Whether the outcome is still changing on the target. */
  isPending: (status: JobStatus) => PENDING.has(status),

  /**
   * The badge palette for a deployment status, in the console's vocabulary (`DeploymentStatusRules`): a roll back is
   * degraded (amber); a failure, a failed roll back and an interruption are red; a refusal changed nothing (neutral).
   */
  statusVariant: (status: JobStatus): StatusVariant =>
    status === JobStatus.Succeeded
      ? 'success'
      : status === JobStatus.RolledBack
        ? 'warning'
        : status === JobStatus.Failed || status === JobStatus.RollbackFailed || status === JobStatus.Interrupted
          ? 'danger'
          : PENDING.has(status)
            ? 'info'
            : 'neutral',

  /** The dot palette for a status, matching its badge. */
  statusTone: (status: JobStatus): IndicatorTone => INDICATOR[DeploymentExtensions.statusVariant(status)],

  /** The badge palette for a target condition. */
  conditionVariant: (condition: TargetCondition): StatusVariant =>
    condition === TargetCondition.Ready ? 'success' : condition === TargetCondition.Running ? 'info' : 'danger',

  /** Human text for a snake-case status or condition. */
  label: (value: string) => value.replaceAll('_', ' '),

  /** How far a running release trails the product's newest published release. */
  releaseDrift: (current: string | null | undefined, releases: ReleaseArtifact[], product: string): ReleaseDrift => {
    const catalog = releases
      .filter((release) => release.product === product)
      .sort((a, b) => Date.parse(b.publishedAt) - Date.parse(a.publishedAt));
    const latest = catalog[0] ?? null;
    const index = current ? catalog.findIndex((release) => release.release === current) : -1;
    return { latest, behind: index < 0 ? null : index };
  },
} as const;
