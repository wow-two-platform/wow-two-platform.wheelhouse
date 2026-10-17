/** One local day's finished rollouts. */
export interface ActivityDay {
  /** `YYYY-MM-DD` in the operator's time zone. */
  date: string;
  succeeded: number;
  failed: number;
}

/** Finished rollouts per day over a range; only days with a rollout are listed. */
export interface DeploymentActivity {
  /** The range's first local date. */
  from: string;
  /** The range's last local date. */
  to: string;
  timeZone: string;
  days: ActivityDay[];
}
