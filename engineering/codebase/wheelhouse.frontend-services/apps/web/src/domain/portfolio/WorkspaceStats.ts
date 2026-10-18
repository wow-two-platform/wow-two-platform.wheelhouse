import { ProductCondition, type ProductFacts } from './ProductFacts';
import { successRate, type PortfolioWeek, type PortfolioWorkspace } from './PortfolioSummary';

/** A workspace's figures on its card or row. */
export interface WorkspaceStats {
  /** The products shown, following "Show inactive". */
  readonly products: number;
  readonly failing: number;
  /** Rolled back, degraded or unread. */
  readonly attention: number;
  readonly deploying: number;
  /** Percent of finished rollouts over the summary's weeks that succeeded; null when none finished. */
  readonly successRate: number | null;
  readonly succeeded: number;
  readonly failed: number;
  /** Succeeded and failed rollouts per week; empty until the summary arrives. */
  readonly weekly: readonly PortfolioWeek[];
}

/** A workspace's figures from its shown products' facts and its summary entry. */
export function workspaceStatsOf(
  facts: readonly (ProductFacts | undefined)[],
  summary: PortfolioWorkspace | undefined,
): WorkspaceStats {
  const count = (...conditions: ProductCondition[]) =>
    facts.filter((item) => item !== undefined && conditions.includes(item.condition)).length;
  const succeeded = summary?.succeeded ?? 0;
  const failed = summary?.failed ?? 0;
  return {
    products: facts.length,
    failing: count(ProductCondition.Failing),
    attention: count(ProductCondition.Attention, ProductCondition.Unknown),
    deploying: count(ProductCondition.Deploying),
    successRate: successRate(succeeded, failed),
    succeeded,
    failed,
    weekly: summary?.weekly ?? [],
  };
}
