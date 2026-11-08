/** A deployment's lifecycle, as recorded by the submission and the target runner. */
export const JobStatus = {
  Submitting: 'submitting',
  Queued: 'queued',
  Running: 'running',
  Succeeded: 'succeeded',
  Failed: 'failed',
  RolledBack: 'rolled_back',
  RollbackFailed: 'rollback_failed',
  Interrupted: 'interrupted',
  Rejected: 'rejected',
  /** The owner stopped a files migration before it ended; it changed no release. */
  Cancelled: 'cancelled',
  Unknown: 'unknown',
} as const;
export type JobStatus = (typeof JobStatus)[keyof typeof JobStatus];
