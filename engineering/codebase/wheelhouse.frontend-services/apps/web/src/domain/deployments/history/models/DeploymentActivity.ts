/** One local day's deployments: the finished ones that changed the environment, by the attention they need. */
export interface ActivityDay {
  /** `YYYY-MM-DD` in the owner's time zone. */
  date: string;
  /** The old outcome counts, kept one release. */
  succeeded: number;
  failed: number;
  /** Applied rollouts that need nothing. */
  calm: number;
  /** Applied rollouts worth a look. */
  look: number;
  /** Applied rollouts that need action. */
  act: number;
  /** Deploys that changed nothing, counted when the viewer shows them; they never colour a day. */
  notApplied: number;
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
