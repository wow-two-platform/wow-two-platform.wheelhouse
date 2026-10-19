<script lang="ts">
import type { WorkspaceRollouts } from "@/domain/portfolio";

/** A workspace's rollouts over the filter's range: succeeded and failed per day, week or month. */
export interface WorkspaceTrendProps {
  /** The range's rollouts; null draws the placeholder while they are read. */
  readonly rollouts: WorkspaceRollouts | null;
  /** Stretches the chart across its container. */
  readonly fill?: boolean;
  readonly height?: number;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { BucketSize } from "@/domain/portfolio";
import WeeklyBars, { type WeeklySeries } from "./WeeklyBars.vue";

/**
 * Finished rollouts per column across the workspace's shown products, stacked: successes in a quiet neutral from the
 * baseline, failures in the danger tone above them. Volume reads as height and trouble as red, so a period that went
 * wrong stands out without a second chart; the pills beside it give the numbers. Columns last a day up to a month's
 * range, a week up to about six months, a month beyond; each workspace keeps its own scale.
 */
defineOptions({ name: "WorkspaceTrend" });
const props = withDefaults(defineProps<WorkspaceTrendProps>(), { fill: false, height: 20 });
defineSlots<{}>();
const buckets = computed(() => props.rollouts?.buckets ?? []);
const series = computed<WeeklySeries[]>(() => [
  {
    key: "succeeded",
    label: "succeeded",
    values: buckets.value.map((bucket) => bucket.succeeded),
    color: "color-mix(in oklch, var(--color-muted-foreground) 60%, transparent)",
  },
  {
    key: "failed",
    label: "failed",
    values: buckets.value.map((bucket) => bucket.failed),
    color: "var(--color-destructive)",
  },
]);
const per = computed(() =>
  props.rollouts?.size === BucketSize.Day ? "day" : props.rollouts?.size === BucketSize.Month ? "month" : "week",
);
const label = computed(
  () =>
    `Rollouts per ${per.value}: ${props.rollouts?.succeeded ?? 0} succeeded, ${props.rollouts?.failed ?? 0} failed`,
);
/* Thinner columns for a long run of days, so a month fits a row's column as a quarter of weeks does. */
const barWidth = computed(() => (buckets.value.length > 20 ? 3 : 4));
</script>
<template>
  <WeeklyBars
    :weeks="buckets.map((bucket) => bucket.start)"
    :labels="buckets.map((bucket) => bucket.label)"
    :series="series"
    :label="label"
    unit="rollouts"
    :max="4"
    :fill="props.fill"
    :height="props.height"
    :bar-width="barWidth"
  />
</template>
