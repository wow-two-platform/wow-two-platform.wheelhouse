import type { JobStatus } from '../enums/JobStatus';
import type { DeploymentStep } from './DeploymentStep';
import type { PublishedSite } from './PublishedSite';
import type { ControlRun, DatabaseCopy, DeploymentOperation } from '../controls/ServiceControls';
import type { FilesMigrationRun } from '../controls/FilesMigration';
import type { ServiceRollout } from '../history/ServiceRollout';

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
  /** How the rollout treats each service so far: what it changes, its versions from and to, and how each fared;
   *  present once the target runner planned the rollout. */
  serviceRollout?: ServiceRollout | null;
  /** Set when the submission is a control — a stop, start, restart, reset or run of the release the target runs. */
  operation?: DeploymentOperation | null;
  /** How a run's command ended; set once a run finished, null for every other job. */
  run?: ControlRun | null;
  /** What a files migration kept so far: its import and each finished source's result; null for any other job. */
  filesMigration?: FilesMigrationRun | null;
  /** What a copy kept: its source, both databases and, once it compared them, the tables and rows; null for any other
   *  job. */
  copy?: DatabaseCopy | null;
}
