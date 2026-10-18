import { useAppQuery } from "@/bootstrap/query";
import { portfolioApi } from "@/integration/portfolio";

/** How many weeks the Products page's trends cover. */
export const TrendWeeks = 12;

/** How often the summary is read again while the page is visible. @internal */
const RefreshMs = 60_000;

/**
 * The summary's query key. It sits under the deployment history views, so whatever refreshes those after a rollout
 * — a finished deployment, a redeploy — refreshes the Products page too.
 */
export const PortfolioSummaryKey = (weeks: number, timeZone: string) =>
  ["deployments", "history", "view", "portfolio", weeks, timeZone] as const;

/** The owner's IANA time zone, which weeks start on Monday in. @internal */
function ownerTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
}

/** Every product's environments and weekly rollouts, and every workspace's outcomes per week; read every minute. */
export function usePortfolioSummary(weeks: number = TrendWeeks) {
  const timeZone = ownerTimeZone();
  return useAppQuery({
    key: PortfolioSummaryKey(weeks, timeZone),
    queryFn: ({ signal }) => portfolioApi.getSummary(weeks, timeZone, signal),
    refetchInterval: RefreshMs,
    meta: { suppressGlobalError: true },
  });
}
