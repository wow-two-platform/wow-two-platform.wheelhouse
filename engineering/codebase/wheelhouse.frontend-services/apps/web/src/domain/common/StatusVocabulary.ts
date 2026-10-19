/**
 * The console's one status vocabulary. Every pill, label, mark and count takes its word, tone and icon from here, so a
 * state reads the same on a product card, a deployment row and a day's counts. Each state has its own icon and word;
 * colour is never the only signal.
 */
export const StatusState = {
  /** An environment serving its release with nothing to look at. */
  Healthy: 'healthy',
  /** A rollout that finished as intended. */
  Succeeded: 'succeeded',
  /** A rollout submitting, queued or running. */
  Deploying: 'deploying',
  /** Unreachable or failing; a rollout that failed, or failed to roll back. */
  Failed: 'failed',
  /**
   * Serving, but not as intended: rolled back to the previous release, or restarting, under host pressure or regressed
   * after a deploy. The amber state; the cause rides along in its details.
   */
  Degraded: 'degraded',
  /** A rollout stopped mid-way, then reconciled. */
  Interrupted: 'interrupted',
  /** A submission the runner refused before it changed anything. */
  Refused: 'refused',
  /** No target, or a target with nothing deployed. */
  None: 'none',
  /** Deployed by its own CI rather than by Wheelhouse, as Wheelhouse itself is. */
  Ci: 'ci',
  /** No reading. */
  Unknown: 'unknown',
} as const;
export type StatusState = (typeof StatusState)[keyof typeof StatusState];

/** Each state's word, for labels, assistive names and counts. */
export const StatusWords: Readonly<Record<StatusState, string>> = {
  healthy: 'healthy',
  succeeded: 'succeeded',
  deploying: 'deploying',
  failed: 'failed',
  degraded: 'degraded',
  interrupted: 'interrupted',
  refused: 'refused',
  none: 'not deployed',
  ci: 'deployed by CI',
  unknown: 'no reading',
};

/** How loudly a state speaks. */
export const StatusTone = {
  /** Neutral, with only the icon green: nothing to do. */
  Calm: 'calm',
  /** Blue: something is in progress. */
  Info: 'info',
  /** Amber: serving, but look at it. */
  Warning: 'warning',
  /** Red: broken or stopped. */
  Danger: 'danger',
  /** Muted: nothing happened, or nothing is known. */
  Quiet: 'quiet',
  /** Dashed: nothing there. */
  Empty: 'empty',
} as const;
export type StatusTone = (typeof StatusTone)[keyof typeof StatusTone];

/** Each state's tone. Amber belongs to degraded alone; an interrupted rollout is a failed outcome, so it is red. */
export const StatusTones: Readonly<Record<StatusState, StatusTone>> = {
  healthy: 'calm',
  succeeded: 'calm',
  ci: 'calm',
  deploying: 'info',
  failed: 'danger',
  interrupted: 'danger',
  degraded: 'warning',
  refused: 'quiet',
  unknown: 'quiet',
  none: 'empty',
};
