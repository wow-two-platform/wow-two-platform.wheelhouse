import { computed, type ComputedRef, type InjectionKey } from "vue";
import { useDeploymentTargets } from "@/application/deployments";
import { useServerVitals } from "@/application/servers";
import { useApiConnection } from "@/application/system";
import { EnvironmentHealthRules } from "@/domain/overview";
import {
  EnvironmentPillRules,
  factsOf,
  type PortfolioProduct,
  type PortfolioWorkspace,
  type ProductFacts,
} from "@/domain/portfolio";
import { EnvironmentOrder } from "@/domain/targets";
import { usePortfolioSummary } from "./usePortfolioSummary";

/**
 * The product this console is: its own CI deploys it, so its pills say so and show the version this console runs in
 * the environment it serves.
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
 * Each product's environment pills, condition, last deploy and weekly trend, and each workspace's summary: the targets
 * and vitals the console already reads, joined with the portfolio summary. Pills read health without deployment stats;
 * the summary's latest rollouts carry what failed or rolled back.
 */
export function useProductFacts() {
  const targets = useDeploymentTargets();
  const vitals = useServerVitals();
  const summary = usePortfolioSummary();
  const system = useApiConnection();

  const summaryByProduct = computed(
    () => new Map<string, PortfolioProduct>((summary.data.value?.products ?? []).map((item) => [item.product, item])),
  );
  const summaryByWorkspace = computed(
    () => new Map<string, PortfolioWorkspace>((summary.data.value?.workspaces ?? []).map((item) => [item.workspace, item])),
  );
  /** Facts are computed on demand and kept until a read changes. @internal */
  const cache = computed(() => {
    void targets.data.value;
    void vitals.data.value;
    void summary.data.value;
    void system.version.value;
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
    const isConsole = slug === ConsoleProduct && own.length === 0;
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
        ci: isConsole
          ? { version: system.environment.value === environment ? system.version.value : null }
          : null,
      });
    });
    const facts = factsOf(slug, pills, product);
    cache.value.set(slug, facts);
    return facts;
  }

  return {
    factsOf: factsFor,
    workspaceSummaryOf: (slug: string) => summaryByWorkspace.value.get(slug),
    /** True until the targets first answer: pills draw placeholders. */
    isLoading: computed(() => targets.data.value === undefined && !targets.error.value),
    /** True until the summary first answers: trends draw placeholders. */
    isSummaryLoading: computed(() => summary.data.value === undefined && !summary.error.value),
    /** True until the vitals first answer: health reads as unknown meanwhile. */
    isVitalsLoading: computed(() => vitals.data.value === undefined && !vitals.error.value),
    weeks: computed(() => summary.data.value?.weeks ?? []),
    /** The busiest week of any product, never below four. */
    scales: computed<TrendScales>(() => {
      const data = summary.data.value;
      const productTop = Math.max(
        0,
        ...(data?.products ?? []).flatMap((item) =>
          (data?.weeks ?? []).map((_, week) => item.environments.reduce((sum, environment) => sum + (environment.weekly[week] ?? 0), 0)),
        ),
      );
      return { products: Math.max(MinimumScale, productTop) };
    }),
    refresh: () => Promise.all([targets.refetch(), vitals.refetch(), summary.refetch()]),
  };
}
