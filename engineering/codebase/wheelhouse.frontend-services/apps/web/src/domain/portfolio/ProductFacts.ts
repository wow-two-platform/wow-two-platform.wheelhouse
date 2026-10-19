import { PillState, type EnvironmentPill } from './EnvironmentPill';
import type { PortfolioProduct } from './PortfolioSummary';

/** A product's overall condition: the worst of its environments, for filtering, sorting and the sidebar's mark. */
export const ProductCondition = {
  /** An environment failed. */
  Failing: 'failing',
  /** An environment is degraded: rolled back, or serving under strain. */
  Attention: 'attention',
  /** A rollout is running and nothing is wrong. */
  Deploying: 'deploying',
  /** Something serves and nothing is wrong. */
  Healthy: 'healthy',
  /** A target could not be read and nothing else is known to be wrong. */
  Unknown: 'unknown',
  /** Nothing is deployed anywhere. */
  Idle: 'idle',
} as const;
export type ProductCondition = (typeof ProductCondition)[keyof typeof ProductCondition];

/** Each condition's words. */
export const ProductConditionWords: Readonly<Record<ProductCondition, string>> = {
  failing: 'failing',
  attention: 'degraded',
  deploying: 'deploying',
  healthy: 'healthy',
  unknown: 'no reading',
  idle: 'not deployed',
};

/** One environment's finished rollouts per week, for a product's trend. */
export interface EnvironmentSeries {
  readonly environment: string;
  readonly values: readonly number[];
}

/** What the Products page knows about one product beyond its definition. */
export interface ProductFacts {
  readonly slug: string;
  /** Dev, then prod. */
  readonly pills: readonly EnvironmentPill[];
  readonly condition: ProductCondition;
  /** The newest submission to any environment; null when it was never deployed. */
  readonly lastDeployedAt: string | null;
  /** Finished rollouts per week and environment, dev first, over the trend's weeks. */
  readonly series: readonly EnvironmentSeries[];
}

/** The worst state among a product's pills, as its condition. */
export function conditionOf(pills: readonly EnvironmentPill[]): ProductCondition {
  const has = (...states: PillState[]) => pills.some((pill) => states.includes(pill.state));
  if (has(PillState.Failed)) return ProductCondition.Failing;
  if (has(PillState.Degraded)) return ProductCondition.Attention;
  if (has(PillState.Deploying)) return ProductCondition.Deploying;
  if (has(PillState.Healthy, PillState.Ci)) return ProductCondition.Healthy;
  if (has(PillState.Unknown)) return ProductCondition.Unknown;
  return ProductCondition.Idle;
}

/**
 * A product's facts from its pills and its summary entry, which is absent for a product never deployed. The trend's
 * entry — the same summary over the weeks a page's range covers — gives the weekly series; the summary by default.
 */
export function factsOf(
  slug: string,
  pills: readonly EnvironmentPill[],
  summary: PortfolioProduct | undefined,
  trend: PortfolioProduct | undefined = summary,
): ProductFacts {
  const submitted = (summary?.environments ?? [])
    .map((environment) => environment.lastDeploy?.submittedAt)
    .filter((value): value is string => typeof value === 'string')
    .sort((left, right) => Date.parse(right) - Date.parse(left));
  return {
    slug,
    pills,
    condition: conditionOf(pills),
    lastDeployedAt: submitted[0] ?? null,
    series: pills.map((pill) => ({
      environment: pill.environment,
      values: trend?.environments.find((item) => item.environment === pill.environment)?.weekly ?? [],
    })),
  };
}

/**
 * A product's condition from its latest rollouts alone — no host vitals — for places that must stay cheap to keep
 * current, such as the sidebar: a live rollout is deploying, a failure with no success since is failing, or degraded
 * when it rolled back. Anything else reads healthy once something succeeded, else idle.
 */
export function rolloutConditionOf(summary: PortfolioProduct | undefined): ProductCondition {
  const environments = summary?.environments ?? [];
  const standing = environments.flatMap((environment) => {
    const last = environment.lastDeploy;
    const running = environment.running;
    if (!last) return [];
    if (last.outcome === 'pending') return [ProductCondition.Deploying];
    const superseded = running !== null && Date.parse(running.submittedAt) > Date.parse(last.submittedAt);
    if (last.outcome === 'failed' && !superseded)
      return [last.status === 'rolled_back' ? ProductCondition.Attention : ProductCondition.Failing];
    return [];
  });
  for (const condition of [ProductCondition.Failing, ProductCondition.Attention, ProductCondition.Deploying])
    if (standing.includes(condition)) return condition;
  return environments.some((environment) => environment.running) ? ProductCondition.Healthy : ProductCondition.Idle;
}
