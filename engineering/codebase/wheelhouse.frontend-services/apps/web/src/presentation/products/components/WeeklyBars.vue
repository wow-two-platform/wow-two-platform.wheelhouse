<script lang="ts">
/** One stacked series of a weekly bar chart. */
export interface WeeklySeries {
  readonly key: string;
  /** The series' name in each bar's title, such as `dev`. */
  readonly label: string;
  /** One value per week, aligned with `weeks`. */
  readonly values: readonly number[];
  /** A CSS colour, a token reference such as `var(--color-chart-prod)`. */
  readonly color: string;
}

/** A compact bar chart over periods — weeks by default — one column per period, its series stacked bottom first. */
export interface WeeklyBarsProps {
  /** Each period's first date (`YYYY-MM-DD`), oldest first; an empty list draws the placeholder. */
  readonly weeks: readonly string[];
  /** Each column's title, aligned with `weeks`, such as `Mon, Oct 13`; `Week of {date}` by default. */
  readonly labels?: readonly string[] | undefined;
  /** Stretches the chart across its container's width rather than drawing fixed-width columns. */
  readonly fill?: boolean;
  readonly series: readonly WeeklySeries[];
  /** The chart's accessible summary. */
  readonly label: string;
  /** The unit each bar's title counts, such as `deploys`. */
  readonly unit?: string;
  readonly height?: number;
  /** The value a full-height column stands for, shared by the charts a page compares; the series' own top by default. */
  readonly max?: number | undefined;
  /** Each column's width; the gap between columns is half of it. */
  readonly barWidth?: number;
}

/** @internal The surface gap between stacked segments, so adjacent fills stay apart. */
const SegmentGap = 1;
</script>
<script setup lang="ts">
import { computed } from "vue";
import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";

/**
 * A sparkline of counts per period: thin columns, one per week (or the day or month its labels name), their series
 * stacked from the baseline in the order given, a surface gap between segments, and a faint tick for an empty period
 * so the timeline stays readable. Each column's title names its period and every series' count; the whole chart
 * carries one summary label for assistive technology.
 */
defineOptions({ name: "WeeklyBars" });
const props = withDefaults(defineProps<WeeklyBarsProps>(), {
  unit: "deploys",
  height: 20,
  barWidth: 4,
  labels: undefined,
  fill: false,
});
defineSlots<{}>();

const gap = computed(() => Math.max(1, Math.round(props.barWidth / 2)));
const width = computed(() => Math.max(0, props.weeks.length * (props.barWidth + gap.value) - gap.value));
const totals = computed(() =>
  props.weeks.map((_, week) => props.series.reduce((sum, series) => sum + (series.values[week] ?? 0), 0)),
);
const max = computed(() => Math.max(1, props.max ?? 0, ...totals.value));
const monthDay = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" });

/** Each column's segments, bottom first, and its title. @internal */
const columns = computed(() =>
  props.weeks.map((monday, week) => {
    const x = week * (props.barWidth + gap.value);
    let base = props.height;
    const segments = props.series
      .map((series) => ({ series, value: series.values[week] ?? 0 }))
      .filter((part) => part.value > 0)
      .map((part, index) => {
        const raw = (part.value / max.value) * props.height;
        const height = Math.max(2, raw - (index > 0 ? SegmentGap : 0));
        const y = base - height - (index > 0 ? SegmentGap : 0);
        base = y;
        return { key: part.series.key, color: part.series.color, x, y: Math.max(0, y), height };
      });
    const date = new Date(`${monday}T00:00:00`);
    const parts = props.series.map((series) => `${series.values[week] ?? 0} ${series.label}`).join(", ");
    const period = props.labels?.[week] ?? `Week of ${Number.isNaN(date.getTime()) ? monday : monthDay.format(date)}`;
    return {
      key: monday,
      x,
      segments,
      empty: totals.value[week] === 0,
      title: `${period}: ${parts}`,
    };
  }),
);
</script>
<template>
  <SkeletonState
    v-if="!props.weeks.length"
    class="shrink-0 rounded"
    :style="{ width: props.fill ? '100%' : `${12 * (props.barWidth + gap) - gap}px`, height: `${props.height}px` }"
  />
  <!-- Stretched, the columns keep their proportions across the width; titles answer as before. -->
  <svg
    v-else
    role="img"
    :aria-label="props.label"
    :width="props.fill ? '100%' : width"
    :height="props.height"
    :viewBox="`0 0 ${width} ${props.height}`"
    :preserveAspectRatio="props.fill ? 'none' : undefined"
    class="shrink-0 overflow-visible"
  >
    <g v-for="column in columns" :key="column.key">
      <title>{{ column.title }}</title>
      <!-- A transparent hit area as tall as the chart, so a column's title answers anywhere above it. -->
      <rect :x="column.x - gap / 2" y="0" :width="props.barWidth + gap" :height="props.height" fill="transparent" />
      <rect
        v-if="column.empty"
        :x="column.x"
        :y="props.height - 1"
        :width="props.barWidth"
        height="1"
        class="fill-border-strong/50"
      />
      <rect
        v-for="segment in column.segments"
        :key="segment.key"
        :x="segment.x"
        :y="segment.y"
        :width="props.barWidth"
        :height="segment.height"
        rx="1"
        :fill="segment.color"
      />
    </g>
  </svg>
</template>
