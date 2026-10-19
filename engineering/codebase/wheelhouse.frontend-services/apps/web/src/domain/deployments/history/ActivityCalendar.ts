import type { ActivityDay } from './models/DeploymentActivity';

/** What a day's cell says: no applied rollout, or the loudest attention among its applied, finished ones. */
export type CellTone = 'empty' | 'success' | 'danger' | 'warning';

/** How strongly a cell is filled: 0 for none, then 1, 2–3, 4–6 and 7+ rollouts. */
export type CellLevel = 0 | 1 | 2 | 3 | 4;

/** The inner mark a cell carries, so colour is never the only signal: a half-dot for a look, a cross for action. */
export type CellMark = 'none' | 'half' | 'cross';

/** The counts a day is coloured by; an older answer carries only succeeded and failed. */
export type DayCounts = Pick<ActivityDay, 'succeeded' | 'failed'> & Partial<Pick<ActivityDay, 'calm' | 'look' | 'act' | 'notApplied'>>;

/** One day of the calendar. */
export interface CalendarCell {
  /** `YYYY-MM-DD`. */
  readonly date: string;
  /** 0 for the column's first weekday. */
  readonly row: number;
  /** Applied, finished rollouts that day. */
  readonly applied: number;
  /** Deploys that changed nothing that day, when the viewer shows them; they never colour it. */
  readonly notApplied: number;
  readonly tone: CellTone;
  readonly level: CellLevel;
  readonly mark: CellMark;
  /** While every attempt shows: the day held both rollouts that needed nothing and ones needing a look or action. */
  readonly isMixed: boolean;
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

/**
 * Pure rules of the deployment activity calendar: colour, intensity, marks, labels and layout. A day takes the loudest
 * attention of its applied, finished rollouts; a deploy that changed nothing never colours it.
 */
export const ActivityCalendar = {
  /** The fill step for a count: 1, 2–3, 4–6, 7+. */
  level(count: number): CellLevel {
    return count <= 0 ? 0 : count === 1 ? 1 : count <= 3 ? 2 : count <= 6 ? 3 : 4;
  },

  /** A day's counts by attention: the applied rollouts that need nothing, a look or action. */
  attention(day: DayCounts | undefined): { calm: number; look: number; act: number } {
    if (!day) return { calm: 0, look: 0, act: 0 };
    // An answer from before the status taxonomy counts outcomes only: a failure needs action.
    if (day.calm === undefined && day.look === undefined && day.act === undefined)
      return { calm: day.succeeded, look: 0, act: day.failed };
    return { calm: day.calm ?? 0, look: day.look ?? 0, act: day.act ?? 0 };
  },

  /** A day's colour: the loudest attention among its applied, finished rollouts — green, amber or red. */
  tone(day: DayCounts | undefined): CellTone {
    const { calm, look, act } = ActivityCalendar.attention(day);
    return act > 0 ? 'danger' : look > 0 ? 'warning' : calm > 0 ? 'success' : 'empty';
  },

  /** The count a day's fill reflects: its applied, finished rollouts. */
  count(day: DayCounts | undefined): number {
    const { calm, look, act } = ActivityCalendar.attention(day);
    return calm + look + act;
  },

  /** Whether a day's attempts were mixed: some needed nothing, others a look or action — a failure fixed that day, or
   * products that ended apart. */
  isMixed(day: DayCounts | undefined): boolean {
    const { calm, look, act } = ActivityCalendar.attention(day);
    return calm > 0 && look + act > 0;
  },

  /** The inner mark of a tone: a half-dot for amber, a cross for red. */
  mark(tone: CellTone): CellMark {
    return tone === 'danger' ? 'cross' : tone === 'warning' ? 'half' : 'none';
  },

  /** A day's tooltip: `Tue, Oct 14, 2026: 3 need nothing · 1 needs action · 2 changed nothing`, or `no deployments`. */
  label(date: string, day: DayCounts | undefined): string {
    const { calm, look, act } = ActivityCalendar.attention(day);
    const parts: string[] = [];
    if (calm) parts.push(`${calm} succeeded`);
    if (look) parts.push(`${look} ${look === 1 ? 'needs' : 'need'} a look`);
    if (act) parts.push(`${act} ${act === 1 ? 'needs' : 'need'} action`);
    if (day?.notApplied) parts.push(`${day.notApplied} changed nothing`);
    return `${LONG_DATE.format(utc(date))}: ${parts.length ? parts.join(' · ') : 'no deployments'}`;
  },

  /**
   * Lays a range out as week columns over weekday rows, `weekStart` 1 for Monday. Days outside the range are null, so
   * the first and last columns can be partial; a month label sits over the first column holding its first day. With
   * `isAudit` — the counts cover every attempt — a day whose attempts were mixed says so.
   */
  build(
    from: string,
    to: string,
    days: ReadonlyArray<DayCounts & { readonly date: string }>,
    weekStart: 0 | 1 = 1,
    options: { readonly isAudit?: boolean } = {},
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
        const tone = ActivityCalendar.tone(day);
        cells.push({
          date,
          row,
          applied: ActivityCalendar.count(day),
          notApplied: day?.notApplied ?? 0,
          tone,
          level: tone === 'empty' ? 0 : ActivityCalendar.level(ActivityCalendar.count(day)),
          mark: ActivityCalendar.mark(tone),
          isMixed: Boolean(options.isAudit) && ActivityCalendar.isMixed(day),
          label: ActivityCalendar.label(date, day),
        });
      }
      weeks.push({ start: weekStartDate, cells });
    }
    // A label too close to the next one would overlap it; the later month keeps its place.
    const spaced = months.filter((month, index) => (months[index + 1]?.column ?? Infinity) - month.column >= 3);
    const weekdays = Array.from({ length: 7 }, (_, row) => WEEKDAY.format(new Date(Date.UTC(2026, 0, 4 + weekStart + row))));
    return { weeks, months: spaced, weekdays };
  },

  /** The date an arrow key moves the focus to: a week (one column) for left and right, a day (one row) for up and down. */
  step(date: string, key: string): string | null {
    const days = key === 'ArrowLeft' ? -7 : key === 'ArrowRight' ? 7 : key === 'ArrowUp' ? -1 : key === 'ArrowDown' ? 1 : 0;
    return days === 0 ? null : iso(new Date(utc(date).getTime() + days * DAY_MS));
  },
} as const;
