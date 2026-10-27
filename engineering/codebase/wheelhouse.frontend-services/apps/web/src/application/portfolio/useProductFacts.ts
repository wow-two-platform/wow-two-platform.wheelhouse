import { computed, toValue, type ComputedRef, type InjectionKey, type MaybeRefOrGetter } from "vue";
import { useDeploymentTargets } from "@/application/deployments";
import { ProductKeys } from "@/application/products";
import { requestLiveVitals, useServerVitals } from "@/application/servers";
import { useApiConnection } from "@/application/system";
import { EnvironmentHealthRules } from "@/domain/overview";
import type { DeploymentRange } from "@/domain/deployments";
import {
  EnvironmentPillRules,
  factsOf,
  TrendWindows,
  type PortfolioProduct,
  type ProductFacts,
} from "@/domain/portfolio";
import { EnvironmentOrder } from "@/domain/targets";
import { productsApi } from "@/integration/products";
import { useAppQuery } from "@/bootstrap/query";
import { usePortfolioSummary } from "./usePortfolioSummary";

/**
 * The product this console is. Whether a product's pills say CI comes from its stored deploy mode; this only says whose
 * CI pills may show the version this console runs in the environment it serves.
 */
export const ConsoleProduct = "wheelhouse";

/** The weekly count a full product trend column stands for, shared by every product chart on a page so they compare. */
export interface TrendScales {
  readonly products: number;
}

/** Where a page provides its trend scales to the charts below it. */
export const TrendScalesKey: InjectionKey<ComputedRef<TrendScales>> = Symbol("TrendScales");

/** The smallest scale, so a product that deploys once a week does not draw full columns. @internal */
const MinimumScale = 4;

/**
 * Each product's environment pills, condition, last deploy and weekly trend: the targets and vitals the console already
 * reads, joined with the portfolio summary. Pills read health without deployment stats; the summary's latest rollouts
 * carry what failed or rolled back. Pills always read as of now; a range — the Products page's — sets the weeks the
 * trends cover, twelve weeks to now without one.
 */
export function useProductFacts(range: MaybeRefOrGetter<DeploymentRange | null> = null) {
  const targets = useDeploymentTargets();
  const vitals = useServerVitals();
  const summary = usePortfolioSummary();
  const trendWindow = computed(() => {
    const value = toValue(range);
    return value ? TrendWindows.of(value, new Date()) : null;
  });
  /* Without a range the trends read the pills' own summary; with one, the same summary over the range's weeks. */
  const trend = toValue(range) === null ? summary : usePortfolioSummary(trendWindow);
  const system = useApiConnection();
  /* The product list every page reads already: each product's deploy mode decides whether its pills say CI. */
  const products = useAppQuery({ key: ProductKeys.list, queryFn: ({ signal }) => productsApi.listProducts(signal) });

  const byProduct = (data: typeof summary.data.value) =>
    new Map<string, PortfolioProduct>((data?.products ?? []).map((item) => [item.product, item]));
  const summaryByProduct = computed(() => byProduct(summary.data.value));
  const trendByProduct = computed(() => byProduct(trend.data.value));
  /** Facts are computed on demand and kept until a read changes. @internal */
  const cache = computed(() => {
    void targets.data.value;
    void vitals.data.value;
    void summary.data.value;
    void trend.data.value;
    void system.version.value;
    void products.data.value;
    return new Map<string, ProductFacts>();
  });

  /** One product's facts; undefined until the targets first answer. */
  function factsFor(slug: string): ProductFacts | undefined {
    const all = targets.data.value;
    if (!all) return undefined;
    const cached = cache.value.get(slug);
    if (cached) return cached;
    const own = all.filter((target) => target.product === slug);
    const readings = EnvironmentHealthRules.ofProduct(slug, own, { vitals: vitals.data.value });
    const product = summaryByProduct.value.get(slug);
    const ci = {
      deploy: products.data.value?.find((item) => item.slug === slug)?.deploy,
      targets: own.length,
      console: slug === ConsoleProduct ? { environment: system.environment.value, version: system.version.value } : null,
    };
    const pills = EnvironmentOrder.map((environment) => {
      const reading = readings.find((item) => item.environment === environment);
      const reported = reading?.targetId
        ? (vitals.data.value?.targets.find((item) => item.targetId === reading.targetId)?.release ?? null)
        : null;
      return EnvironmentPillRules.of({
        environment,
        reading,
        summary: product?.environments.find((item) => item.environment === environment),
        reportedRelease: reported,
        ci: EnvironmentPillRules.ciOf(ci, environment),
      });
    });
    const facts = factsOf(slug, pills, product, trendByProduct.value.get(slug));
    cache.value.set(slug, facts);
    return facts;
  }

  return {
    factsOf: factsFor,
    /** True until the targets first answer: pills draw placeholders. */
    isLoading: computed(() => targets.data.value === undefined && !targets.error.value),
    /** True until the vitals first answer: health reads as unknown meanwhile. */
    isVitalsLoading: computed(() => vitals.data.value === undefined && !vitals.error.value),
    /** The trends' weeks, each its Monday; empty until the trend's summary answers. */
    weeks: computed(() => trend.data.value?.weeks ?? []),
    /** The busiest week of any product, never below four. */
    scales: computed<TrendScales>(() => {
      const data = trend.data.value;
      const productTop = Math.max(
        0,
        ...(data?.products ?? []).flatMap((item) =>
          (data?.weeks ?? []).map((_, week) => item.environments.reduce((sum, environment) => sum + (environment.weekly[week] ?? 0), 0)),
        ),
      );
      return { products: Math.max(MinimumScale, productTop) };
    }),
    /** The page's explicit Refresh: the hosts are read now, not the snapshot the console shares between tabs. */
    refresh: () => {
      requestLiveVitals();
      return Promise.all([targets.refetch(), vitals.refetch(), summary.refetch(), ...(trend === summary ? [] : [trend.refetch()])]);
    },
  };
}
