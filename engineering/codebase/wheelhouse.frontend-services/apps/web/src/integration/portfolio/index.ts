import type { PortfolioSummary } from "@/domain/portfolio";
import { requestData } from "@/integration/common";
import { PortfolioSummarySchema } from "./schemas";

/** The Products page's portfolio-wide read. */
export const portfolioApi = {
  /** Every product's environments and weekly rollouts, and every workspace's outcomes per week, over `weeks` weeks
   *  starting on Monday in the IANA time zone `tz`. */
  getSummary: (weeks: number, timeZone: string, signal?: AbortSignal) =>
    requestData<PortfolioSummary>(
      `/api/portfolio/summary?${new URLSearchParams({ weeks: String(weeks), tz: timeZone }).toString()}`,
      PortfolioSummarySchema,
      { signal },
    ),
};
