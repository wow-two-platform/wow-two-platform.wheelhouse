import { toValue, type MaybeRefOrGetter } from "vue";
import { useAppQuery } from "@/bootstrap/query";
import type { TrendWindow } from "@/domain/portfolio";
import { portfolioApi } from "@/integration/portfolio";

/** How many weeks the summary covers by default: the sidebar's marks and Home's trends. */
export const TrendWeeks = 12;

/** How often the summary is read again while the page is visible. @internal */
const RefreshMs = 60_000;

/**
 * The summary's query key. It sits under the deployment history views, so whatever refreshes those after a rollout
 * — a finished deployment, a redeploy — refreshes the Products page too.
 */
export const PortfolioSummaryKey = (weeks: number, timeZone: string, to: string | null = null) =>
  to
    ? (["deployments", "history", "view", "portfolio", weeks, timeZone, to] as const)
    : (["deployments", "history", "view", "portfolio", weeks, timeZone] as const);

/** The owner's IANA time zone, which weeks start on Monday in. @internal */
function ownerTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
}

/**
 * Every product's environments and weekly rollouts, and every workspace's outcomes per week; read every minute. A
 * window sets the weeks and, for a range in the past, the instant its last week holds; twelve weeks to now without one.
 */
export function usePortfolioSummary(window: MaybeRefOrGetter<TrendWindow | null> = null) {
  const timeZone = ownerTimeZone();
  const current = () => toValue(window) ?? { weeks: TrendWeeks, to: null };
  return useAppQuery({
    key: () => PortfolioSummaryKey(current().weeks, timeZone, current().to),
    queryFn: ({ signal }) => portfolioApi.getSummary(current().weeks, timeZone, current().to, signal),
    refetchInterval: RefreshMs,
    meta: { suppressGlobalError: true },
    keepPreviousData: true,
  });
}
