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
  /** Serving a mix of releases: the latest rollout stopped part-way or could restore only some services. */
  Mixed: 'mixed',
  /** No target, or a target with nothing deployed. */
  None: 'none',
  /** Deployed by its own CI rather than by Wheelhouse, as Wheelhouse itself is. */
  Ci: 'ci',
  /** Stopped by the owner, whole or some of its services: on purpose, never a failure. */
  Stopped: 'stopped',
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
  mixed: 'mixed',
  none: 'not deployed',
  ci: 'deployed by CI',
  stopped: 'stopped',
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

/** Each state's tone. A mix of releases needs action, so it is red like a failure. */
export const StatusTones: Readonly<Record<StatusState, StatusTone>> = {
  healthy: 'calm',
  succeeded: 'calm',
  ci: 'calm',
  deploying: 'info',
  failed: 'danger',
  mixed: 'danger',
  degraded: 'warning',
  stopped: 'quiet',
  unknown: 'quiet',
  none: 'empty',
};

/**
 * A deployment's state: what its environment runs because of it. Eight states, mutually exclusive by impact; the API
 * carries these names. Each has its own icon and word; its colour follows the attention it needs, not the state.
 */
export const DeployState = {
  /** The environment is changing: scheduled, queued, waiting or rolling out. */
  InProgress: 'in_progress',
  /** The new release runs, healthy. */
  Succeeded: 'succeeded',
  /** The new release runs, not all of it healthy. */
  Degraded: 'degraded',
  /** The previous release runs again, whole. */
  RolledBack: 'rolled_back',
  /** A mix of releases runs. */
  Partial: 'partial',
  /** Broken or unknown; needs action. */
  Failed: 'failed',
  /** Unchanged: refused, replaced, cancelled, expired or stopped before it changed anything. */
  NotApplied: 'not_applied',
  /** No readable receipt. */
  Unknown: 'unknown',
} as const;
export type DeployState = (typeof DeployState)[keyof typeof DeployState];

/** Each deployment state's word. */
export const DeployStateWords: Readonly<Record<DeployState, string>> = {
  in_progress: 'In progress',
  succeeded: 'Succeeded',
  degraded: 'Degraded',
  rolled_back: 'Rolled back',
  partial: 'Partial',
  failed: 'Failed',
  not_applied: 'Not applied',
  unknown: 'Unknown',
};

/** How much attention a deployment needs; its colour follows this, not its state. */
export const Attention = {
  /** Nothing to do: neutral, the icon green when it succeeded. */
  None: 'none',
  /** In progress: blue. */
  Info: 'info',
  /** Worth a look: amber. */
  Look: 'look',
  /** Needs action: red. */
  Act: 'act',
  /** Nothing known: muted. */
  Muted: 'muted',
} as const;
export type Attention = (typeof Attention)[keyof typeof Attention];

/** Each attention's tone. */
export const AttentionTones: Readonly<Record<Attention, StatusTone>> = {
  none: 'calm',
  info: 'info',
  look: 'warning',
  act: 'danger',
  muted: 'quiet',
};

/** The way a deployment reached its state, as the chip writes it after the state's word. */
export const SubstateWords: Readonly<Record<string, string>> = {
  scheduled: 'scheduled',
  queued: 'queued',
  waiting: 'waiting',
  checking: 'checking',
  draining: 'draining',
  maintenance: 'maintenance',
  migrating: 'migrating',
  starting: 'starting',
  verifying: 'verifying',
  rolling_back: 'rolling back',
  watching: 'watching',
  passed: 'passed',
  unverified: 'unverified',
  replaced: 'replaced',
  at_deploy: 'at deploy',
  in_watch: 'in watch',
  side_effect: 'side effect',
  automatic: 'automatic',
  together: 'together',
  stopped_mid_rollout: 'stopped mid-rollout',
  partly_rolled_back: 'partly rolled back',
  cancelled_after_changes: 'cancelled after changes',
  rollback_failed: 'rollback failed',
  rollback_not_allowed: 'rollback not allowed',
  interrupted: 'interrupted',
  first_deploy_unhealthy: 'first deploy unhealthy',
  refused: 'refused',
  cancelled: 'cancelled',
  superseded: 'superseded',
  expired: 'expired',
  aborted: 'aborted',
  check_failed: 'check failed',
  no_receipt: 'no receipt',
  unreadable: 'unreadable',
};

/** How one service ended in a deployment; the API carries these names. */
export const ServiceResult = {
  Updated: 'updated',
  New: 'new',
  Unchanged: 'unchanged',
  NotReached: 'not_reached',
  Restored: 'restored',
  Failed: 'failed',
  Affected: 'affected',
  Waiting: 'waiting',
  Starting: 'starting',
  Unknown: 'unknown',
} as const;
export type ServiceResult = (typeof ServiceResult)[keyof typeof ServiceResult];

/** Each service result's word. */
export const ServiceResultWords: Readonly<Record<ServiceResult, string>> = {
  updated: 'updated',
  new: 'new',
  unchanged: 'unchanged',
  not_reached: 'not reached',
  restored: 'restored',
  failed: 'failed',
  affected: 'affected',
  waiting: 'waiting',
  starting: 'starting',
  unknown: 'unknown',
};

/** Each service result's tone. */
export const ServiceResultTones: Readonly<Record<ServiceResult, StatusTone>> = {
  updated: 'calm',
  new: 'calm',
  unchanged: 'quiet',
  not_reached: 'empty',
  restored: 'warning',
  failed: 'danger',
  affected: 'warning',
  waiting: 'info',
  starting: 'info',
  unknown: 'quiet',
};

/** Why a service changed in a rollout, in a word: `config` for a definition change (memory, health check, mounts). */
export const ChangeReasonWords: Readonly<Record<string, string>> = {
  added: 'added',
  image: 'image',
  definition: 'config',
  settings: 'settings',
};
