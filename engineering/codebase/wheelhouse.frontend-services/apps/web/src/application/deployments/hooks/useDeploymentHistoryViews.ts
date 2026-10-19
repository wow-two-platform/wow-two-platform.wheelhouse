import { computed, toValue, watch, type MaybeRefOrGetter } from "vue";

import { useShowAllAttempts } from "@/application/preferences";
import { useAppInfiniteQuery, useAppQuery, useInvalidate } from "@/bootstrap/query";
import {
  DeploymentFilters,
  type DeploymentFilterState,
  type DeploymentItem,
  type DeploymentPage,
  type HistoryView,
} from "@/domain/deployments";
import { deploymentHistoryApi } from "@/integration/deployments/historyApi";

import { DeploymentKeys } from "../DeploymentKeys";

/** How often live rollouts are read while the page is visible. */
const RunningPollMilliseconds = 3000;

/** How often the issues are read while the page is visible. */
const IssuesPollMilliseconds = 60_000;

/** The owner's IANA time zone, which the calendar counts days in. */
export function ownerTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
}

/** What the viewer's Show all attempts toggle asks the history for: every attempt, or each target's latest of a day. */
export function useHistoryView() {
  const everyAttempt = useShowAllAttempts();
  return computed<HistoryView>(() => ({ everyAttempt: everyAttempt.value, timeZone: ownerTimeZone() }));
}

/** Every attempt in the owner's time zone: what a read the toggle does not govern takes. */
function everyAttempt(): HistoryView {
  return { everyAttempt: true, timeZone: ownerTimeZone() };
}

/**
 * The filter's deployments, newest first, one cursor page at a time: each target's latest deploy of a day, or every
 * attempt while the viewer's toggle shows them (`everyAttempt` reads every attempt whatever it says). A preset range
 * resolves when each page is read; the cursor holds the position, so a later page never repeats or skips an older
 * deployment. `day` narrows to one local calendar day, as the grid's expanded day does.
 */
export function useDeploymentPages(
  filter: MaybeRefOrGetter<DeploymentFilterState>,
  options: {
    readonly limit?: number;
    readonly day?: MaybeRefOrGetter<string | null>;
    readonly enabled?: MaybeRefOrGetter<boolean>;
    readonly everyAttempt?: boolean;
  } = {},
) {
  const limit = options.limit ?? 25;
  const day = () => toValue(options.day ?? null);
  const viewer = useHistoryView();
  const view = () => (options.everyAttempt ? everyAttempt() : viewer.value);
  return useAppInfiniteQuery<DeploymentItem, DeploymentPage>({
    key: () => DeploymentKeys.historyPage(`${DeploymentFilters.key(toValue(filter), view())}|${day() ?? ""}`, limit),
    queryFn: ({ pageParam, signal }) => {
      const date = day();
      const params = DeploymentFilters.params(toValue(filter), new Date(), {
        view: view(),
        ...(date ? { bounds: DeploymentFilters.dayBounds(date) } : {}),
      });
      return deploymentHistoryApi.listPage(params, typeof pageParam === "string" ? pageParam : null, limit, signal);
    },
    initialPageParam: null,
    getNextPageParam: (page) => page.nextCursor ?? undefined,
    mapPage: (page) => page.items,
    enabled: () => toValue(options.enabled ?? true),
  });
}

/** Finished rollouts per local day over the filter's range, in the owner's time zone: each target's latest of a day, or
 * every attempt with the deploys that changed nothing beside them while the viewer shows them. */
export function useDeploymentActivity(filter: MaybeRefOrGetter<DeploymentFilterState>, enabled: MaybeRefOrGetter<boolean> = true) {
  const view = useHistoryView();
  return useAppQuery({
    key: () => DeploymentKeys.activity(DeploymentFilters.key(toValue(filter), view.value), view.value.timeZone),
    queryFn: ({ signal }) =>
      deploymentHistoryApi.getActivity(
        DeploymentFilters.params(toValue(filter), new Date(), { view: view.value }),
        view.value.timeZone,
        signal,
      ),
    enabled: () => toValue(enabled),
    keepPreviousData: true,
  });
}

/** The filter's measures over its range beside the range before it, following the viewer's toggle as the records do:
 * each target's latest deploy of a day, or every attempt with the deploys that changed nothing. Either way they say how
 * many a latest-of-day view hides (`notApplied` and `superseded`). */
export function useDeploymentRangeStats(filter: MaybeRefOrGetter<DeploymentFilterState>) {
  const view = useHistoryView();
  return useAppQuery({
    key: () => DeploymentKeys.rangeStats(DeploymentFilters.key(toValue(filter), view.value)),
    queryFn: ({ signal }) =>
      deploymentHistoryApi.getStats(DeploymentFilters.params(toValue(filter), new Date(), { view: view.value }), signal),
    keepPreviousData: true,
  });
}

/** Entity-only query parameters: running rollouts and promotions take no range. @internal */
function entityParams(entities: readonly string[]): Array<[string, string]> {
  return DeploymentFilters.params({ entities, range: { kind: "preset", preset: "24h" } }, new Date()).filter(
    ([key]) => key !== "from" && key !== "to",
  );
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

/** Per product, the newest release that succeeded on dev and is not what prod runs; read while `enabled` holds. */
export function useDeploymentPromotions(
  entities: MaybeRefOrGetter<readonly string[]> = [],
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  return useAppQuery({
    key: () => DeploymentKeys.promotions([...toValue(entities)].sort().join(",")),
    queryFn: ({ signal }) => deploymentHistoryApi.listPromotions(entityParams(toValue(entities)), signal),
    enabled: () => toValue(enabled),
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
