import { ProductCondition, type ProductFacts } from './ProductFacts';
import { successRate } from './PortfolioSummary';
import { RolloutBuckets, type BucketSize, type RolloutBucket, type RolloutDays } from './RolloutBuckets';

/** The finished rollouts of a workspace's shown products over the filter's range. */
export interface WorkspaceRollouts {
  readonly succeeded: number;
  readonly failed: number;
  /** Percent of finished rollouts that succeeded; null when none finished. */
  readonly successRate: number | null;
  /** How long each chart column lasts. */
  readonly size: BucketSize;
  /** The range's columns, oldest first. */
  readonly buckets: readonly RolloutBucket[];
}

/** A workspace's figures on its card or row: what its shown products are now, and their rollouts over the range. */
export interface WorkspaceStats {
  /** The products shown: the ones the filter keeps, following "Show inactive". */
  readonly products: number;
  readonly failing: number;
  /** Degraded: rolled back, or serving under strain. */
  readonly degraded: number;
  readonly deploying: number;
  /** Their rollouts over the range; null until the range is read. */
  readonly rollouts: WorkspaceRollouts | null;
}

/** A workspace's figures from its shown products' facts and their rollouts per day over the range. */
export function workspaceStatsOf(
  facts: readonly (ProductFacts | undefined)[],
  activity: RolloutDays | undefined,
): WorkspaceStats {
  const count = (...conditions: ProductCondition[]) =>
    facts.filter((item) => item !== undefined && conditions.includes(item.condition)).length;
  return {
    products: facts.length,
    failing: count(ProductCondition.Failing),
    degraded: count(ProductCondition.Attention),
    deploying: count(ProductCondition.Deploying),
    rollouts: activity ? rolloutsOf(activity) : null,
  };
}

/** The range's totals, rate and columns. @internal */
function rolloutsOf(activity: RolloutDays): WorkspaceRollouts {
  const succeeded = activity.days.reduce((sum, day) => sum + day.succeeded, 0);
  const failed = activity.days.reduce((sum, day) => sum + day.failed, 0);
  return {
    succeeded,
    failed,
    successRate: successRate(succeeded, failed),
    size: RolloutBuckets.sizeOf(activity.from, activity.to),
    buckets: RolloutBuckets.of(activity),
  };
}
