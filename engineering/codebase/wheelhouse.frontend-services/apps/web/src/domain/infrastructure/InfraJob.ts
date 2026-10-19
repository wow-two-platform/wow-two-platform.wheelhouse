import type { StatusState } from '@/domain/common';

/** A recorded infrastructure job's state; a server's enrollment and a product's placement share it. */
export const InfraJobStatus = {
  /** No enrollment yet; a placement never reads this. */
  NotStarted: 'not_started',
  Queued: 'queued',
  Running: 'running',
  Succeeded: 'succeeded',
  Failed: 'failed',
  /** Stopped part-way, such as by a restart; a rerun continues from the first step that has not succeeded. */
  Interrupted: 'interrupted',
} as const;
export type InfraJobStatus = (typeof InfraJobStatus)[keyof typeof InfraJobStatus];

/** One step's state inside a job. */
export const JobStepStatus = {
  Pending: 'pending',
  Running: 'running',
  Succeeded: 'succeeded',
  Failed: 'failed',
  Skipped: 'skipped',
} as const;
export type JobStepStatus = (typeof JobStepStatus)[keyof typeof JobStepStatus];

/** One recorded step of a job; its title comes from the host and shows as is. */
export interface JobStep {
  key: string;
  title: string;
  status: JobStepStatus;
  detail: string | null;
  startedAt: string | null;
  completedAt: string | null;
}

/** A single-use tailnet key a job minted: its ID, tags and expiry only, never the key. */
export interface MintedKey {
  keyId: string;
  tags: string[];
  expires: string;
  description?: string | undefined;
  mintedAt?: string | undefined;
  step?: string | undefined;
  node?: string | undefined;
}

/** How often a queued or running job is read again. */
export const JOB_POLL_MS = 2_000;

const Words: Readonly<Record<InfraJobStatus, string>> = {
  not_started: 'Not started',
  queued: 'Queued',
  running: 'Running',
  succeeded: 'Succeeded',
  failed: 'Failed',
  interrupted: 'Interrupted',
};

const States: Readonly<Record<InfraJobStatus, StatusState>> = {
  not_started: 'none',
  queued: 'deploying',
  running: 'deploying',
  succeeded: 'succeeded',
  failed: 'failed',
  interrupted: 'degraded',
};

const StepWords: Readonly<Record<JobStepStatus, string>> = {
  pending: 'waiting',
  running: 'running',
  succeeded: 'done',
  failed: 'failed',
  skipped: 'skipped',
};

/** Pure rules over enrollment and placement jobs. */
export const InfraJobRules = {
  /** Whether the job still runs, so its record is read again. */
  isActive: (status: InfraJobStatus | null | undefined): boolean =>
    status === InfraJobStatus.Queued || status === InfraJobStatus.Running,

  /** The read interval while the job runs, else no polling. */
  pollInterval: (status: InfraJobStatus | null | undefined): number | false =>
    InfraJobRules.isActive(status) ? JOB_POLL_MS : false,

  /** Whether any of the jobs still runs. */
  anyActive: (jobs: ReadonlyArray<{ readonly status: InfraJobStatus }> | null | undefined): boolean =>
    (jobs ?? []).some((job) => InfraJobRules.isActive(job.status)),

  /** Whether a job seen running has finished since: what refreshes the views its outcome changes. */
  hasFinished: (before: InfraJobStatus | null | undefined, after: InfraJobStatus | null | undefined): boolean =>
    InfraJobRules.isActive(before) && after != null && !InfraJobRules.isActive(after),

  /** Whether the job stopped short and the same call runs it again from its first unfinished step. */
  canRetry: (status: InfraJobStatus | null | undefined): boolean =>
    status === InfraJobStatus.Failed || status === InfraJobStatus.Interrupted,

  /** The job's word. */
  word: (status: InfraJobStatus): string => Words[status],

  /** The job's state in the console's status vocabulary, for its pill. */
  state: (status: InfraJobStatus): StatusState => States[status],

  /** A step's word, for assistive technology. */
  stepWord: (status: JobStepStatus): string => StepWords[status],

  /** A step's seconds: finished, or elapsed until `now` while it runs; null before it starts. */
  stepSeconds: (step: Pick<JobStep, 'status' | 'startedAt' | 'completedAt'>, now: number = Date.now()): number | null => {
    if (!step.startedAt) return null;
    const start = Date.parse(step.startedAt);
    const end = step.completedAt ? Date.parse(step.completedAt) : step.status === JobStepStatus.Running ? now : NaN;
    const seconds = (end - start) / 1000;
    return Number.isFinite(seconds) && seconds >= 0 ? seconds : null;
  },

  /** Whether one of the steps runs now, so its elapsed time ticks. */
  hasRunningStep: (steps: readonly JobStep[]): boolean => steps.some((step) => step.status === JobStepStatus.Running),

  /** A digest's leading characters, enough to tell kits apart: `3f9a0c2b7e11`. */
  shortDigest: (digest: string | null | undefined, length = 12): string => (digest ? digest.slice(0, length) : ''),
} as const;
