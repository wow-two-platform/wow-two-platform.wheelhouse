import { computed, toValue, type MaybeRefOrGetter } from "vue";
import { ResultExtensions } from "@wow-two-beta/fe/foundation/results";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";
import { ownerTimeZone } from "@/application/deployments/hooks/useDeploymentHistoryViews";
import { useAppQuery } from "@/bootstrap/query";
import {
  DeploymentFilters,
  EntityKeys,
  StatusFilter,
  type DeploymentActivity,
  type DeploymentFilterState,
  type DeploymentRange,
} from "@/domain/deployments";
import type { RolloutDays } from "@/domain/portfolio";
import { deploymentHistoryApi } from "@/integration/deployments/historyApi";

/** One workspace card's subject: its key and the products it shows, by slug. */
export interface WorkspaceProducts {
  readonly key: string;
  readonly products: readonly string[];
}

/** How often the figures are read again while the page is visible, as the portfolio summary is. @internal */
const RefreshMs = 60_000;

/** A local `YYYY-MM-DD`. @internal */
const localDate = (date: Date) => DeploymentFilters.localDate(date);

/**
 * Each workspace card's finished rollouts per day over the Products page's range, counting only the products the card
 * shows — the ones the filter keeps — so its counts, rate and chart describe what is listed below it. One read asks for
 * every card at once, under the history views' key, so a finished rollout refreshes it; while a new filter or range is
 * read, the previous figures stay. A card without products reads nothing and gets the range's empty days.
 */
export function useWorkspaceActivity(
  workspaces: MaybeRefOrGetter<readonly WorkspaceProducts[]>,
  range: MaybeRefOrGetter<DeploymentRange>,
) {
  const tz = ownerTimeZone();
  const filters = computed(() =>
    toValue(workspaces)
      .filter((item) => item.products.length > 0)
      .map((item) => ({
        key: item.key,
        filter: {
          entities: [...item.products].sort().map(EntityKeys.product),
          status: StatusFilter.All,
          range: toValue(range),
        } satisfies DeploymentFilterState,
      })),
  );
  const read = useAppQuery({
    key: () => [
      ...DeploymentKeys.historyViews,
      "workspace-activity",
      tz,
      ...filters.value.map((item) => `${item.key}=${DeploymentFilters.key(item.filter)}`),
    ],
    queryFn: async ({ signal }) => {
      const asked = filters.value;
      const now = new Date();
      const results = await Promise.all(
        asked.map((item) => deploymentHistoryApi.getActivity(DeploymentFilters.params(item.filter, now), tz, signal)),
      );
      const byKey = new Map<string, DeploymentActivity>();
      for (const [index, result] of results.entries()) {
        if (!result.ok) return result;
        byKey.set(asked[index]!.key, result.value);
      }
      return ResultExtensions.ok(byKey);
    },
    enabled: () => filters.value.length > 0,
    refetchInterval: RefreshMs,
    keepPreviousData: true,
    meta: { suppressGlobalError: true },
  });
  /** The range's dates with nothing in them, for a card without products. @internal */
  const empty = computed<RolloutDays>(() => {
    const { from, to } = DeploymentFilters.resolve(toValue(range), new Date());
    return { from: localDate(from), to: localDate(new Date(Math.max(from.getTime(), to.getTime() - 1))), days: [] };
  });

  return {
    /** A card's rollouts per day; undefined until its first read answers. */
    activityOf: (key: string, products: number): RolloutDays | undefined =>
      products === 0 ? empty.value : read.data.value?.get(key),
    /** True while the shown figures belong to the previous filter or range. */
    isStale: read.isPreviousData,
    error: read.error,
    refresh: () => read.refetch(),
  };
}
