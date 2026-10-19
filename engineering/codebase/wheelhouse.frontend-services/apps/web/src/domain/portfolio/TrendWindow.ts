import { DeploymentFilters, type DeploymentRange } from '@/domain/deployments/history/DeploymentFilter';

/** The weeks a product trend covers for a range: the portfolio summary's `weeks` and, for a past range, its `to`. */
export interface TrendWindow {
  /** Monday-first weeks, the one holding `to` last. */
  readonly weeks: number;
  /** The instant the last week holds, ISO; null for a range ending now. */
  readonly to: string | null;
}

/** @internal The most weeks the summary serves. */
const MaxWeeks = 52;
const DAY_MS = 86_400_000;

/** The local midnight of the Monday of a date's week. @internal */
function mondayOf(date: Date): Date {
  const midnight = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  midnight.setDate(midnight.getDate() - ((midnight.getDay() + 6) % 7));
  return midnight;
}

/**
 * Pure rules fitting product trends to the Products page's range. Trends count rollouts per week, so a range covers
 * every week it touches: 24 hours on a Monday covers last week and this one. At most 52 weeks, the summary's limit.
 */
export const TrendWindows = {
  of(range: DeploymentRange, now: Date): TrendWindow {
    const { from, to } = DeploymentFilters.resolve(range, now);
    const last = new Date(Math.max(from.getTime(), to.getTime() - 1));
    const span = Math.round((mondayOf(last).getTime() - mondayOf(from).getTime()) / (7 * DAY_MS)) + 1;
    return {
      weeks: Math.min(MaxWeeks, Math.max(1, span)),
      to: range.kind === 'custom' ? to.toISOString() : null,
    };
  },
} as const;
