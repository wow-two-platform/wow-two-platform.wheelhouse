/** One local day's finished rollouts. */
export interface ActivityDay {
  /** `YYYY-MM-DD` in the owner's time zone. */
  date: string;
  succeeded: number;
  failed: number;
}

/** One product's finished rollouts over the range. */
export interface ActivityProduct {
  /** The product's slug. */
  product: string;
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
  /** The range's busiest products, most finished rollouts first; at most five. */
  products: ActivityProduct[];
}
