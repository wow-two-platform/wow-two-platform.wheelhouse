import type { JobStatus } from '../../enums/JobStatus';
import type { ReleaseKind } from '../../models/ReleaseArtifact';

/** What a deployment status amounts to: refusals changed nothing, pending ones are still changing. */
export const DeploymentOutcome = {
  Succeeded: 'succeeded',
  Failed: 'failed',
  Refused: 'refused',
  Pending: 'pending',
  Unknown: 'unknown',
} as const;
export type DeploymentOutcome = (typeof DeploymentOutcome)[keyof typeof DeploymentOutcome];

/** The step a live rollout is on. */
export interface CurrentStep {
  name: string;
  status?: string | null;
  startedAt?: string | null;
  /** The step's position in the rollout, from 1. */
  index?: number | null;
}

/** One deployment as lists show it: what went where, how it went and what it changed. */
export interface DeploymentItem {
  id: string;
  targetId: string | null;
  /** Null for a target the inventory no longer holds. */
  environment: string | null;
  product: string | null;
  workspace: string | null;
  release: string | null;
  /** The bundle a redeploy submits again. */
  bundleId: string | null;
  kind: ReleaseKind | null;
  branch: string | null;
  sourceCommit: string | null;
  actor: string | null;
  status: JobStatus;
  outcome: DeploymentOutcome;
  reason: string | null;
  warnings: string[];
  submittedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  durationSeconds: number | null;
  /** The services whose image changed; null when the runner no longer holds the bundle. */
  changedServices: string[] | null;
  currentStep: CurrentStep | null;
}
