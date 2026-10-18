import { computed, toValue, watch, type MaybeRefOrGetter } from "vue";

import { useAppInfiniteQuery, useAppQuery, useInvalidate } from "@/bootstrap/query";
import {
  DeploymentFilters,
  type DeploymentFilterState,
  type DeploymentItem,
  type DeploymentPage,
} from "@/domain/deployments";
import { deploymentHistoryApi } from "@/integration/deployments/historyApi";

import { DeploymentKeys } from "../DeploymentKeys";

/** How often live rollouts are read while the page is visible. */
const RunningPollMilliseconds = 3000;

/** How often the issues are read while the page is visible. */
const IssuesPollMilliseconds = 60_000;

/** The operator's IANA time zone, which the calendar counts days in. */
export function operatorTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
}

/**
 * The filter's deployments, newest first, one cursor page at a time. A preset range resolves when each page is read;
 * the cursor holds the position, so a later page never repeats or skips an older deployment. `day` narrows to one local
 * calendar day, as the grid's expanded day does.
 */
export function useDeploymentPages(
  filter: MaybeRefOrGetter<DeploymentFilterState>,
  options: { readonly limit?: number; readonly day?: MaybeRefOrGetter<string | null>; readonly enabled?: MaybeRefOrGetter<boolean> } = {},
) {
  const limit = options.limit ?? 25;
  const day = () => toValue(options.day ?? null);
  return useAppInfiniteQuery<DeploymentItem, DeploymentPage>({
    key: () => DeploymentKeys.historyPage(`${DeploymentFilters.key(toValue(filter))}|${day() ?? ""}`, limit),
    queryFn: ({ pageParam, signal }) => {
      const date = day();
      const params = DeploymentFilters.params(
        toValue(filter),
        new Date(),
        date ? { bounds: DeploymentFilters.dayBounds(date) } : {},
      );
      return deploymentHistoryApi.listPage(params, typeof pageParam === "string" ? pageParam : null, limit, signal);
    },
    initialPageParam: null,
    getNextPageParam: (page) => page.nextCursor ?? undefined,
    mapPage: (page) => page.items,
    enabled: () => toValue(options.enabled ?? true),
  });
}

/** Finished rollouts per local day over the filter's range, in the operator's time zone. */
export function useDeploymentActivity(filter: MaybeRefOrGetter<DeploymentFilterState>, enabled: MaybeRefOrGetter<boolean> = true) {
  const tz = operatorTimeZone();
  return useAppQuery({
    key: () => DeploymentKeys.activity(DeploymentFilters.key(toValue(filter)), tz),
    queryFn: ({ signal }) => deploymentHistoryApi.getActivity(DeploymentFilters.params(toValue(filter), new Date()), tz, signal),
    enabled: () => toValue(enabled),
    keepPreviousData: true,
  });
}

/** The filter's measures over its range beside the range before it; the status filter does not apply. */
export function useDeploymentRangeStats(filter: MaybeRefOrGetter<DeploymentFilterState>) {
  const keyed = () => ({ ...toValue(filter), status: "all" as const });
  return useAppQuery({
    key: () => DeploymentKeys.rangeStats(DeploymentFilters.key(keyed())),
    queryFn: ({ signal }) => deploymentHistoryApi.getStats(DeploymentFilters.params(keyed(), new Date(), { withStatus: false }), signal),
    keepPreviousData: true,
  });
}

/** Entity-only query parameters: running rollouts and promotions take no range. @internal */
function entityParams(entities: readonly string[]): Array<[string, string]> {
  return DeploymentFilters.params({ entities, status: "all", range: { kind: "preset", preset: "24h" } }, new Date(), {
    withStatus: false,
  }).filter(([key]) => key !== "from" && key !== "to");
}

/**
 * Live rollouts with the step each is on, read every three seconds while the page is visible. When a rollout leaves
 * the list it has finished, so the history views, the measures and that deployment's detail are read again.
 */
export function useRunningDeployments(entities: MaybeRefOrGetter<readonly string[]> = []) {
  const invalidate = useInvalidate();
  const running = useAppQuery({
    key: () => DeploymentKeys.running([...toValue(entities)].sort().join(",")),
    queryFn: ({ signal }) => deploymentHistoryApi.listRunning(entityParams(toValue(entities)), signal),
    refetchInterval: RunningPollMilliseconds,
    meta: { suppressGlobalError: true },
  });
  watch(
    () => running.data.value?.map((item) => item.id) ?? null,
    (now, before) => {
      if (!now || !before) return;
      const finished = before.filter((id) => !now.includes(id));
      if (!finished.length) return;
      void invalidate(DeploymentKeys.historyViews);
      void invalidate(DeploymentKeys.statsAll);
      for (const id of finished) void invalidate(DeploymentKeys.detail(id));
    },
  );
  return running;
}

/** Per product, the newest release that succeeded on dev and is not what prod runs. */
export function useDeploymentPromotions(entities: MaybeRefOrGetter<readonly string[]> = []) {
  return useAppQuery({
    key: () => DeploymentKeys.promotions([...toValue(entities)].sort().join(",")),
    queryFn: ({ signal }) => deploymentHistoryApi.listPromotions(entityParams(toValue(entities)), signal),
  });
}

/**
 * The targets that need attention, read again with the history views when a rollout finishes, and every minute while
 * the page is visible, since a rollout turns stuck by time alone.
 */
export function useDeploymentIssues(entities: MaybeRefOrGetter<readonly string[]> = []) {
  return useAppQuery({
    key: () => DeploymentKeys.issues([...toValue(entities)].sort().join(",")),
    queryFn: ({ signal }) => deploymentHistoryApi.listIssues(entityParams(toValue(entities)), signal),
    refetchInterval: IssuesPollMilliseconds,
  });
}

/** One deployment in full, from the control plane's records; never contacts the target. */
export function useDeploymentDetail(id: MaybeRefOrGetter<string | null>) {
  const key = computed(() => toValue(id));
  return useAppQuery({
    key: () => DeploymentKeys.detail(key.value ?? ""),
    queryFn: ({ signal }) => deploymentHistoryApi.getDetail(key.value ?? "", signal),
    enabled: () => Boolean(key.value),
    meta: { suppressGlobalError: true },
  });
}
