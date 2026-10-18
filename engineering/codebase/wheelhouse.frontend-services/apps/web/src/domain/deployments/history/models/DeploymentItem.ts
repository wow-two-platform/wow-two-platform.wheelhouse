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

/** The watch window after a successful rollout: its containers and sites looked at once a minute. */
export interface DeploymentWatch {
  status: 'watching' | 'passed' | 'degraded' | 'superseded' | 'unverified';
  until: string | null;
  checks: number | null;
  /** What regressed: a container that stopped, turned unhealthy or restarted, or a site gone silent. */
  findings: string[];
  degradedAt: string | null;
  completedAt: string | null;
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
  /** The owner's one-line why for the deploy, when one was given. */
  deployReason: string | null;
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
  /** The watch after a successful rollout; null when it was not watched. */
  watch: DeploymentWatch | null;
}
