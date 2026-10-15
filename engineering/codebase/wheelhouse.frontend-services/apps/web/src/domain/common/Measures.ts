import { formatBytes, formatDuration } from '@wow-two-beta/ui-vue/foundation/formatters';

const DAY_MS = 86_400_000;
const MOMENT = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });

/** Pure display rules for sizes, durations, ratios and instants; relative times render through the SDK `RelativeTime`. */
export const Measures = {
  /** Bytes in binary units, through the SDK formatter: `512 MiB`, `1.5 GiB`. */
  bytes: (value: number | null | undefined) =>
    value == null || !Number.isFinite(value) ? '—' : formatBytes(value, { binary: true }),

  /** Whole seconds in their two largest non-zero units, through the SDK formatter: `45s`, `3m 20s`, `3d 4h`. */
  duration: (seconds: number | null | undefined) =>
    seconds == null || !Number.isFinite(seconds) ? '—' : formatDuration(Math.round(seconds) * 1000),

  /** A 0–1 ratio as a whole percentage. */
  percent: (ratio: number | null | undefined) => (ratio == null ? '—' : `${Math.round(ratio * 100)}%`),

  /** Whole days from an ISO instant to `now`. */
  ageDays: (iso: string, now: number = Date.now()) => Math.max(0, Math.floor((now - Date.parse(iso)) / DAY_MS)),

  /** An instant as short local text for dense tables: `Sep 26, 11:10 AM`. */
  moment: (iso: string | null | undefined) => (iso ? MOMENT.format(new Date(iso)) : '—'),

  /** The used share of a capacity, 0–100. */
  usedPercent: (total: number, free: number) => (total > 0 ? ((total - free) / total) * 100 : 0),
} as const;
