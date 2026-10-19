/** One rollout of this console's own target, read back from the host's journal. */
export interface SelfDeploymentRun {
  id: string;
  release: string | null;
  /** The release the target ran before it, when known. */
  previousRelease: string | null;
  /** The runner status, such as `running`, `succeeded` or `rolled_back`. */
  status: string;
  /** What the status amounts to: `succeeded`, `failed`, `pending` or `unknown`. */
  outcome: string;
  actor: string | null;
  /** The owner's why, when one was given. */
  reason: string | null;
  /** The display-safe reason it failed or rolled back. */
  failure: string | null;
  submittedAt: string;
  completedAt: string | null;
  /** Submitted before this console started: the rollout that started it. */
  beforeBoot: boolean;
}

/** This console's own deployment: what replaced it, or what is replacing it (decision 64). */
export interface SelfDeployment {
  /** The environment this console serves. */
  environment: string;
  /** Its own target; null when the inventory holds none. */
  target: string | null;
  bootedAt: string;
  latest: SelfDeploymentRun | null;
}
