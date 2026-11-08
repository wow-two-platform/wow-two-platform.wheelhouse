/** How one step of a rollout went, as the target runner recorded it. */
export const StepStatus = {
  Running: 'running',
  Succeeded: 'succeeded',
  Failed: 'failed',
  /** The step finished, but something needs a look: a site that did not answer through the ingress. */
  Warning: 'warning',
  Skipped: 'skipped',
  /** The owner cancelled the job while the step ran. */
  Cancelled: 'cancelled',
} as const;
export type StepStatus = (typeof StepStatus)[keyof typeof StepStatus];
