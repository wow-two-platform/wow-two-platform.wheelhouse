<script lang="ts">
import type { WorkspaceStats } from "@/domain/portfolio";

/** A workspace's success trend: succeeded and failed rollouts per week. */
export interface WorkspaceTrendProps {
  readonly stats: WorkspaceStats | undefined;
  readonly weeks: readonly string[];
  /** The weekly count a full column stands for; the workspace's own busiest week, never below four, by default. */
  readonly scale?: number | undefined;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import WeeklyBars, { type WeeklySeries } from "./WeeklyBars.vue";

/**
 * Finished rollouts per week across the workspace, stacked: successes in a quiet neutral from the baseline, failures
 * in the danger tone above them. Volume reads as height and trouble as red, so a week that went wrong stands out
 * without a second chart; the success rate beside it gives the number. Each workspace keeps its own scale.
 */
defineOptions({ name: "WorkspaceTrend" });
const props = defineProps<WorkspaceTrendProps>();
defineSlots<{}>();
/* Each workspace on its own scale: its chart's job is the shape of its trouble over time, not its volume beside
   another workspace's. */
const scale = computed(() => props.scale ?? 4);
const series = computed<WeeklySeries[]>(() => [
  {
    key: "succeeded",
    label: "succeeded",
    values: (props.stats?.weekly ?? []).map((week) => week.succeeded),
    color: "color-mix(in oklch, var(--color-muted-foreground) 60%, transparent)",
  },
  {
    key: "failed",
    label: "failed",
    values: (props.stats?.weekly ?? []).map((week) => week.failed),
    color: "var(--color-destructive)",
  },
]);
const label = computed(
  () => `Rollouts over ${props.weeks.length} weeks: ${props.stats?.succeeded ?? 0} succeeded, ${props.stats?.failed ?? 0} failed`,
);
</script>
<template>
  <WeeklyBars
    :weeks="props.stats?.weekly.length ? props.weeks : []"
    :series="series"
    :label="label"
    unit="rollouts"
    :max="scale"
  />
</template>
