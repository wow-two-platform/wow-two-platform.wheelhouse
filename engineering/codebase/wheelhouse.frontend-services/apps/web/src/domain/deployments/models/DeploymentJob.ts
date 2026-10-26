import type { JobStatus } from '../enums/JobStatus';
import type { DeploymentStep } from './DeploymentStep';
import type { PublishedSite } from './PublishedSite';
import type { DeploymentOperation } from '../controls/ServiceControls';

/** One submitted deployment with its last observed outcome; reasons never carry values. */
export interface DeploymentJob {
  id: string;
  targetId?: string | null;
  bundleId?: string | null;
  release?: string | null;
  actor?: string | null;
  submittedAt?: string;
  status: JobStatus;
  reason?: string | null;
  failure?: string | null;
  startedAt?: string;
  completedAt?: string;
  mutationStarted?: boolean;
  /** The rollout's steps so far; present once the target runner picked the job up. */
  steps?: DeploymentStep[];
  /** What needs a look although the rollout succeeded. */
  warnings?: string[];
  sites?: PublishedSite[];
  /** Set when the submission is a control — a stop, start or restart of the release the target runs. */
  operation?: DeploymentOperation | null;
}
