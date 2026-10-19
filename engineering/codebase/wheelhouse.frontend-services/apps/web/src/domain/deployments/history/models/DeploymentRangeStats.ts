/** Outcome counts, success rate, and rollout and recovery times over one range. */
export interface RangeMeasures {
  from: string;
  to: string;
  deploys: number;
  succeeded: number;
  failed: number;
  refused: number;
  pending: number;
  /** Succeeded share of finished rollouts; null without one. */
  successRate: number | null;
  medianRolloutSeconds: number | null;
  /** Median time from a target's failure to its next success. */
  medianRecoverySeconds: number | null;
  /** Deploys that changed nothing. */
  notApplied: number;
  /** Deploys worth a look. */
  look: number;
  /** Deploys that need action. */
  act: number;
  /** Applied deploys a later one replaced on the same target that day: the attempts a latest-of-day view hides. */
  superseded: number;
}

/** A range's measures beside the same measures for the range of equal length before it. */
export interface DeploymentRangeStats {
  current: RangeMeasures;
  /** Null when the range before holds no deployment. */
  previous: RangeMeasures | null;
}
