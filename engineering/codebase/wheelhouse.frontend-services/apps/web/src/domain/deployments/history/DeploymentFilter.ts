import { EntityKeys } from './EntitySelection';

/** Which deployments a view shows by the attention they need: all, those needing action, those worth a look, or the
 * successes that need nothing. Deploys that changed nothing follow the viewer's one Show unchanged switch instead. */
export const StatusFilter = {
  All: 'all',
  NeedsAction: 'needs-action',
  Look: 'look',
  Succeeded: 'succeeded',
} as const;
export type StatusFilter = (typeof StatusFilter)[keyof typeof StatusFilter];

/** Reads a status filter from an address value; the old `failed` reads as needs-action, anything else as all. */
export function readStatusFilter(value: string | null | undefined): StatusFilter {
  if (value === 'failed') return StatusFilter.NeedsAction;
  return (Object.values(StatusFilter) as string[]).includes(value ?? '') ? (value as StatusFilter) : StatusFilter.All;
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

/** Which deployments a view shows: entity keys (`w:` workspace, `p:` product, `s:` product/service), outcomes, range. */
export interface DeploymentFilterState {
  readonly entities: readonly string[];
  readonly status: StatusFilter;
  readonly range: DeploymentRange;
}

/** The view a filter opens with: everything, every outcome, six months. */
export const DefaultFilter: DeploymentFilterState = { entities: [], status: StatusFilter.All, range: DefaultRange };

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
   * The API's query parameters for a filter: repeated `workspace`, `product` and `service`, then `status` (left out
   * for all, and when `withStatus` is false), `notApplied` when the viewer shows the deploys that changed nothing, then
   * ISO `from` and `to` resolved at `now`.
   */
  params(
    filter: DeploymentFilterState,
    now: Date,
    options: { readonly withStatus?: boolean; readonly bounds?: { from: Date; to: Date }; readonly unchanged?: boolean } = {},
  ): Array<[string, string]> {
    const result: Array<[string, string]> = [];
    for (const key of filter.entities) {
      const entity = EntityKeys.parse(key);
      if (!entity) continue;
      if (entity.kind === 'workspace') result.push(['workspace', entity.workspace]);
      else if (entity.kind === 'product') result.push(['product', entity.product]);
      else result.push(['service', `${entity.product}/${entity.service}`]);
    }
    if ((options.withStatus ?? true) && filter.status !== StatusFilter.All) result.push(['status', filter.status]);
    if (options.unchanged) result.push(['notApplied', 'true']);
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
   * A stable text of a filter for cache keys: the preset, not its instants, so a key holds while time moves; `unchanged`
   * marks a read that includes the deploys that changed nothing.
   */
  key(filter: DeploymentFilterState, unchanged = false): string {
    const range = filter.range.kind === 'preset' ? filter.range.preset : `${filter.range.from}..${filter.range.to}`;
    return `${[...filter.entities].sort().join(',')}|${filter.status}|${range}${unchanged ? '|unchanged' : ''}`;
  },
} as const;
