import { StatusFilter } from './DeploymentFilter';
import type { ActivityDay } from './models/DeploymentActivity';

/** What a day's cell says: no rollout, all succeeded, all failed, or both. */
export type CellTone = 'empty' | 'success' | 'danger' | 'warning';

/** How strongly a cell is filled: 0 for none, then 1, 2–3, 4–6 and 7+ rollouts. */
export type CellLevel = 0 | 1 | 2 | 3 | 4;

/** The inner mark a cell carries, so colour is never the only signal: failed days and mixed days have one. */
export type CellMark = 'none' | 'failed' | 'mixed';

/** One day of the calendar. */
export interface CalendarCell {
  /** `YYYY-MM-DD`. */
  readonly date: string;
  /** 0 for the column's first weekday. */
  readonly row: number;
  readonly succeeded: number;
  readonly failed: number;
  readonly tone: CellTone;
  readonly level: CellLevel;
  readonly mark: CellMark;
  /** The tooltip and accessible name: date and counts. */
  readonly label: string;
}

/** One column of the calendar: a week, with null where the week falls outside the range. */
export interface CalendarWeek {
  readonly start: string;
  readonly cells: ReadonlyArray<CalendarCell | null>;
}

/** A month label and the column it starts over. */
export interface CalendarMonth {
  readonly label: string;
  readonly column: number;
}

/** A range's finished rollouts. */
export interface RangeTotals {
  readonly succeeded: number;
  readonly failed: number;
}

/** The calendar: week columns over weekday rows, month labels above. */
export interface Calendar {
  readonly weeks: readonly CalendarWeek[];
  readonly months: readonly CalendarMonth[];
  /** The weekday labels, top row first. */
  readonly weekdays: readonly string[];
}

const DAY_MS = 86_400_000;
const LONG_DATE = new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' });
const MONTH = new Intl.DateTimeFormat(undefined, { month: 'short', timeZone: 'UTC' });
const WEEKDAY = new Intl.DateTimeFormat(undefined, { weekday: 'short', timeZone: 'UTC' });

/** A `YYYY-MM-DD` date as a UTC midnight, so arithmetic never meets a daylight-saving change. @internal */
function utc(date: string): Date {
  const [year, month, day] = date.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(year, month - 1, day));
}

/** A UTC midnight as `YYYY-MM-DD`. @internal */
function iso(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/** Pure rules of the deployment activity calendar: colour, intensity, marks, labels and layout. */
export const ActivityCalendar = {
  /** The fill step for a count: 1, 2–3, 4–6, 7+. */
  level(count: number): CellLevel {
    return count <= 0 ? 0 : count === 1 ? 1 : count <= 3 ? 2 : count <= 6 ? 3 : 4;
  },

  /**
   * A day's colour under the status filter: all failed red, all succeeded green, both amber. With Failed on, only
   * failures colour a day (red scale); with Succeeded on, only successes.
   */
  tone(day: Pick<ActivityDay, 'succeeded' | 'failed'> | undefined, status: StatusFilter = StatusFilter.All): CellTone {
    const succeeded = status === StatusFilter.Failed ? 0 : (day?.succeeded ?? 0);
    const failed = status === StatusFilter.Succeeded ? 0 : (day?.failed ?? 0);
    if (succeeded > 0 && failed > 0) return 'warning';
    if (failed > 0) return 'danger';
    return succeeded > 0 ? 'success' : 'empty';
  },

  /** The count a day's fill reflects under the status filter. */
  count(day: Pick<ActivityDay, 'succeeded' | 'failed'> | undefined, status: StatusFilter = StatusFilter.All): number {
    if (!day) return 0;
    return status === StatusFilter.Failed ? day.failed : status === StatusFilter.Succeeded ? day.succeeded : day.succeeded + day.failed;
  },

  /** The inner mark of a tone. */
  mark(tone: CellTone): CellMark {
    return tone === 'danger' ? 'failed' : tone === 'warning' ? 'mixed' : 'none';
  },

  /** A day's tooltip: `Tue, Oct 14, 2026: 3 succeeded · 1 failed`, or `no deployments`. */
  label(date: string, day: Pick<ActivityDay, 'succeeded' | 'failed'> | undefined, status: StatusFilter = StatusFilter.All): string {
    const parts: string[] = [];
    if (status !== StatusFilter.Failed && day?.succeeded) parts.push(`${day.succeeded} succeeded`);
    if (status !== StatusFilter.Succeeded && day?.failed) parts.push(`${day.failed} failed`);
    return `${LONG_DATE.format(utc(date))}: ${parts.length ? parts.join(' · ') : 'no deployments'}`;
  },

  /**
   * Lays a range out as week columns over weekday rows, `weekStart` 1 for Monday. Days outside the range are null, so
   * the first and last columns can be partial; a month label sits over the first column holding its first day.
   */
  build(
    from: string,
    to: string,
    days: readonly ActivityDay[],
    status: StatusFilter = StatusFilter.All,
    weekStart: 0 | 1 = 1,
  ): Calendar {
    const byDate = new Map(days.map((day) => [day.date, day]));
    const first = utc(from);
    const last = utc(to);
    const offset = (first.getUTCDay() - weekStart + 7) % 7;
    const start = new Date(first.getTime() - offset * DAY_MS);
    const weeks: CalendarWeek[] = [];
    const months: CalendarMonth[] = [];
    for (let column = 0, cursor = start; cursor <= last; column++) {
      const cells: Array<CalendarCell | null> = [];
      const weekStartDate = iso(cursor);
      for (let row = 0; row < 7; row++, cursor = new Date(cursor.getTime() + DAY_MS)) {
        if (cursor < first || cursor > last) {
          cells.push(null);
          continue;
        }
        const date = iso(cursor);
        if (cursor.getUTCDate() === 1 || (months.length === 0 && column === 0 && row === offset))
          if (!months.some((month) => month.column === column)) months.push({ label: MONTH.format(cursor), column });
        const day = byDate.get(date);
        const tone = ActivityCalendar.tone(day, status);
        cells.push({
          date,
          row,
          succeeded: day?.succeeded ?? 0,
          failed: day?.failed ?? 0,
          tone,
          level: tone === 'empty' ? 0 : ActivityCalendar.level(ActivityCalendar.count(day, status)),
          mark: ActivityCalendar.mark(tone),
          label: ActivityCalendar.label(date, day, status),
        });
      }
      weeks.push({ start: weekStartDate, cells });
    }
    // A label too close to the next one would overlap it; the later month keeps its place.
    const spaced = months.filter((month, index) => (months[index + 1]?.column ?? Infinity) - month.column >= 3);
    const weekdays = Array.from({ length: 7 }, (_, row) => WEEKDAY.format(new Date(Date.UTC(2026, 0, 4 + weekStart + row))));
    return { weeks, months: spaced, weekdays };
  },

  /** The finished rollouts across the range's days. */
  totals(days: ReadonlyArray<Pick<ActivityDay, 'succeeded' | 'failed'>>): RangeTotals {
    return days.reduce((sum, day) => ({ succeeded: sum.succeeded + day.succeeded, failed: sum.failed + day.failed }), {
      succeeded: 0,
      failed: 0,
    });
  },

  /** The date an arrow key moves the focus to: a week (one column) for left and right, a day (one row) for up and down. */
  step(date: string, key: string): string | null {
    const days = key === 'ArrowLeft' ? -7 : key === 'ArrowRight' ? 7 : key === 'ArrowUp' ? -1 : key === 'ArrowDown' ? 1 : 0;
    return days === 0 ? null : iso(new Date(utc(date).getTime() + days * DAY_MS));
  },
} as const;
