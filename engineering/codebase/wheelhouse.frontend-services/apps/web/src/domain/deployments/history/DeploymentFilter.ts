import { EntityKeys } from './EntitySelection';

/**
 * What a history read keeps. Off, each target's latest applied deploy of a local day: earlier attempts that day and the
 * deploys that changed nothing stay hidden, so a day whose last deploy succeeded reads as a success. On, every attempt
 * and every deploy that changed nothing. The viewer's one Show all attempts toggle decides it; the measures always read
 * every attempt.
 */
export interface HistoryView {
  /** Every attempt, the deploys that changed nothing included; off keeps each target's latest deploy of a day. */
  readonly everyAttempt: boolean;
  /** The IANA time zone a day is counted in. */
  readonly timeZone: string;
}

/** The ranges a view offers in one click, ending now. */
export const RangePreset = {
  Day: '24h',
  Week: '7d',
  Month: '30d',
  Quarter: '90d',
  HalfYear: '6m',
  Year: '12m',
} as const;
export type RangePreset = (typeof RangePreset)[keyof typeof RangePreset];

/** The presets in the order the picker lists them, with their labels. */
export const RangePresets: ReadonlyArray<{ readonly value: RangePreset; readonly label: string; readonly short: string }> = [
  { value: RangePreset.Day, label: 'Last 24 hours', short: '24 h' },
  { value: RangePreset.Week, label: 'Last 7 days', short: '7 d' },
  { value: RangePreset.Month, label: 'Last 30 days', short: '30 d' },
  { value: RangePreset.Quarter, label: 'Last 90 days', short: '90 d' },
  { value: RangePreset.HalfYear, label: 'Last 6 months', short: '6 months' },
  { value: RangePreset.Year, label: 'Last 12 months', short: '12 months' },
];

/** The preset a view opens with: the calendar's six months. */
export const DefaultPreset: RangePreset = RangePreset.HalfYear;

/** The range a view opens with. */
export const DefaultRange: DeploymentRange = { kind: 'preset', preset: DefaultPreset };

/** A view's time range: a preset ending now, or fixed local wall-clock bounds (`YYYY-MM-DDTHH:mm`). */
export type DeploymentRange =
  | { readonly kind: 'preset'; readonly preset: RangePreset }
  | { readonly kind: 'custom'; readonly from: string; readonly to: string };

/** Which deployments a view shows: entity keys (`w:` workspace, `p:` product, `s:` product/service) and the range. Which
 * attempts show follows the viewer's Show all attempts toggle (`HistoryView`), not the filter. */
export interface DeploymentFilterState {
  readonly entities: readonly string[];
  readonly range: DeploymentRange;
}

/** The view a filter opens with: everything over six months. */
export const DefaultFilter: DeploymentFilterState = { entities: [], range: DefaultRange };

/** A local wall-clock instant as the custom range writes it. */
export const LocalMinutePattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/;

const HOUR_MS = 3_600_000;
const DAY_MS = 24 * HOUR_MS;

/** Two digits. @internal */
const pad = (value: number) => String(value).padStart(2, '0');

/** Pure rules over a deployment filter: its instants, its query parameters and its label. */
export const DeploymentFilters = {
  /** The instants a range covers: from inclusive, to exclusive. A preset ends at `now`. */
  resolve(range: DeploymentRange, now: Date): { from: Date; to: Date } {
    if (range.kind === 'custom') return { from: new Date(range.from), to: new Date(range.to) };
    const to = new Date(now.getTime());
    const from = new Date(now.getTime());
    switch (range.preset) {
      case RangePreset.Day:
        return { from: new Date(now.getTime() - DAY_MS), to };
      case RangePreset.Week:
        return { from: new Date(now.getTime() - 7 * DAY_MS), to };
      case RangePreset.Month:
        return { from: new Date(now.getTime() - 30 * DAY_MS), to };
      case RangePreset.Quarter:
        return { from: new Date(now.getTime() - 90 * DAY_MS), to };
      case RangePreset.HalfYear:
        from.setMonth(from.getMonth() - 6);
        return { from, to };
      case RangePreset.Year:
        from.setMonth(from.getMonth() - 12);
        return { from, to };
    }
  },

  /** Whether a custom range is well formed: both bounds, from before to. */
  isValidCustom(from: string, to: string): boolean {
    if (!LocalMinutePattern.test(from) || !LocalMinutePattern.test(to)) return false;
    const start = new Date(from).getTime();
    const end = new Date(to).getTime();
    return Number.isFinite(start) && Number.isFinite(end) && start < end;
  },

  /** A Date as the custom range writes it: local `YYYY-MM-DDTHH:mm`. */
  localMinute(date: Date): string {
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  },

  /** A local calendar date `YYYY-MM-DD`. */
  localDate(date: Date): string {
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  },

  /** The local midnight that starts a `YYYY-MM-DD` date, and the next one. */
  dayBounds(date: string): { from: Date; to: Date } {
    const [year, month, day] = date.split('-').map(Number) as [number, number, number];
    return { from: new Date(year, month - 1, day), to: new Date(year, month - 1, day + 1) };
  },

  /**
   * The API's query parameters for a filter: repeated `workspace`, `product` and `service`, then, for a `view`, either
   * `notApplied` (every attempt) or `latest` (each target's latest deploy of a day) with the `tz` days are counted in,
   * then ISO `from` and `to` resolved at `now`.
   */
  params(
    filter: DeploymentFilterState,
    now: Date,
    options: { readonly bounds?: { from: Date; to: Date }; readonly view?: HistoryView } = {},
  ): Array<[string, string]> {
    const result: Array<[string, string]> = [];
    for (const key of filter.entities) {
      const entity = EntityKeys.parse(key);
      if (!entity) continue;
      if (entity.kind === 'workspace') result.push(['workspace', entity.workspace]);
      else if (entity.kind === 'product') result.push(['product', entity.product]);
      else result.push(['service', `${entity.product}/${entity.service}`]);
    }
    if (options.view) {
      result.push(options.view.everyAttempt ? ['notApplied', 'true'] : ['latest', 'true']);
      result.push(['tz', options.view.timeZone]);
    }
    const { from, to } = options.bounds ?? DeploymentFilters.resolve(filter.range, now);
    result.push(['from', from.toISOString()], ['to', to.toISOString()]);
    return result;
  },

  /** The trigger text of a range: the preset's label, or both bounds as short local text. */
  label(range: DeploymentRange): string {
    if (range.kind === 'preset') return RangePresets.find((item) => item.value === range.preset)?.label ?? range.preset;
    const format = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
    return `${format.format(new Date(range.from))} – ${format.format(new Date(range.to))}`;
  },

  /**
   * A stable text of a filter for cache keys: the preset, not its instants, so a key holds while time moves; a `view`
   * marks whether the read keeps every attempt or each day's latest, and in which time zone.
   */
  key(filter: DeploymentFilterState, view?: HistoryView): string {
    const range = filter.range.kind === 'preset' ? filter.range.preset : `${filter.range.from}..${filter.range.to}`;
    const kept = view ? (view.everyAttempt ? '|every' : `|latest@${view.timeZone}`) : '';
    return `${[...filter.entities].sort().join(',')}|${range}${kept}`;
  },
} as const;
