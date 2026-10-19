import type { ActivityDay } from '@/domain/deployments/history/models/DeploymentActivity';

/** How long one column of a range's rollout chart lasts. */
export const BucketSize = { Day: 'day', Week: 'week', Month: 'month' } as const;
export type BucketSize = (typeof BucketSize)[keyof typeof BucketSize];

/** One column of a range's rollout chart. */
export interface RolloutBucket {
  /** The column's first date, `YYYY-MM-DD`: the day, the week's Monday or the month's first. */
  readonly start: string;
  /** What the column's title names: a day, the week of a Monday, or a month. */
  readonly label: string;
  readonly succeeded: number;
  readonly failed: number;
}

/** What the buckets are cut from: the activity read's local dates and its days with a rollout. */
export interface RolloutDays {
  /** The range's first local date. */
  readonly from: string;
  /** The range's last local date. */
  readonly to: string;
  readonly days: readonly ActivityDay[];
}

/** @internal The longest range drawn a day per column, then a week per column; longer ranges draw months. */
const DailyUpTo = 31;
const WeeklyUpTo = 190;
const DAY_MS = 86_400_000;
const DATE = /^\d{4}-\d{2}-\d{2}$/;
const DAY = new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric', timeZone: 'UTC' });
const MONTH_DAY = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' });
const MONTH = new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric', timeZone: 'UTC' });

/** A `YYYY-MM-DD` date as a UTC midnight, so arithmetic never meets a daylight-saving change. @internal */
function utc(date: string): Date {
  const [year, month, day] = date.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(year, month - 1, day));
}

/** A UTC midnight as `YYYY-MM-DD`. @internal */
function iso(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/** The first date of the column a date falls in. @internal */
function startOf(date: Date, size: BucketSize): Date {
  if (size === BucketSize.Day) return date;
  if (size === BucketSize.Month) return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1));
  return new Date(date.getTime() - ((date.getUTCDay() + 6) % 7) * DAY_MS);
}

/** A column's title. @internal */
function labelOf(start: Date, size: BucketSize): string {
  if (size === BucketSize.Day) return DAY.format(start);
  if (size === BucketSize.Month) return MONTH.format(start);
  return `Week of ${MONTH_DAY.format(start)}`;
}

/**
 * Pure rules cutting a range's daily rollouts into chart columns: a day each up to a month, a week each (Monday first)
 * up to about six months, a month each beyond. Every column of the range is present, empty or not, so the chart keeps
 * the range's shape.
 */
export const RolloutBuckets = {
  /** How long a column lasts for a range of local dates, both inclusive. */
  sizeOf(from: string, to: string): BucketSize {
    const days = Math.round((utc(to).getTime() - utc(from).getTime()) / DAY_MS) + 1;
    return days <= DailyUpTo ? BucketSize.Day : days <= WeeklyUpTo ? BucketSize.Week : BucketSize.Month;
  },

  /** The range's columns, oldest first, with each column's succeeded and failed rollouts. */
  of(activity: RolloutDays): RolloutBucket[] {
    if (!DATE.test(activity.from) || !DATE.test(activity.to) || activity.to < activity.from) return [];
    const size = RolloutBuckets.sizeOf(activity.from, activity.to);
    const columns = new Map<string, { start: Date; succeeded: number; failed: number }>();
    for (let day = utc(activity.from); iso(day) <= activity.to; day = new Date(day.getTime() + DAY_MS)) {
      const start = startOf(day, size);
      const key = iso(start);
      if (!columns.has(key)) columns.set(key, { start, succeeded: 0, failed: 0 });
    }
    for (const day of activity.days) {
      if (!DATE.test(day.date)) continue;
      const column = columns.get(iso(startOf(utc(day.date), size)));
      if (!column) continue;
      column.succeeded += day.succeeded;
      column.failed += day.failed;
    }
    return [...columns.entries()].map(([start, column]) => ({
      start,
      label: labelOf(column.start, size),
      succeeded: column.succeeded,
      failed: column.failed,
    }));
  },
} as const;
