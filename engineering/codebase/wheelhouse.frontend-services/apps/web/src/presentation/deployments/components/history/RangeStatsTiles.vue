<script lang="ts">
import type { DeploymentFilterState } from "@/domain/deployments";

/** Defines the measures tiles. */
export interface RangeStatsTilesProps {
  /** The shared filter; its entities and range apply, its status does not. */
  readonly filter: DeploymentFilterState;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { StatCard, type StatCardTrend } from "@wow-two-beta/fe/presentation/display";
import { DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";

import { useDeploymentRangeStats } from "@/application/deployments";
import { Measures } from "@/domain/common";
import { DeploymentFilters } from "@/domain/deployments";

/**
 * Deploys, success rate, median rollout and median recovery for the filter's products and range, each against the
 * range of equal length before it. Every outcome counts, whatever the status filter shows.
 */
defineOptions({ name: "RangeStatsTiles" });
const props = defineProps<RangeStatsTilesProps>();
const stats = useDeploymentRangeStats(() => props.filter);
const period = computed(() => DeploymentFilters.label(props.filter.range).replace(/^Last /, "").toLowerCase());
const priorLabel = computed(() => (props.filter.range.kind === "preset" ? `vs prior ${period.value}` : "vs prior period"));

const metrics = computed(() => {
  const data = stats.data.value;
  if (!data) return undefined;
  const { current, previous } = data;
  return [
    {
      label: "Deploys",
      value: current.deploys,
      helper: `${current.succeeded} succeeded · ${current.failed} failed · ${current.refused} refused`,
      ...difference(current.deploys, previous?.deploys, (value) => String(value)),
      isLowerBetter: false,
    },
    {
      label: "Success rate",
      value: Measures.percent(current.successRate),
      helper: "Of finished rollouts",
      ...difference(current.successRate, previous?.successRate, (value) => `${Math.round(value * 100)} pts`),
      isLowerBetter: false,
    },
    {
      label: "Median rollout",
      value: Measures.duration(current.medianRolloutSeconds),
      helper: "Start to verified",
      ...difference(current.medianRolloutSeconds, previous?.medianRolloutSeconds, Measures.duration),
      isLowerBetter: true,
    },
    {
      label: "Median recovery",
      value: Measures.duration(current.medianRecoverySeconds),
      helper: "Failure to next success",
      ...difference(current.medianRecoverySeconds, previous?.medianRecoverySeconds, Measures.duration),
      isLowerBetter: true,
    },
  ];
});
const tiles = useSkeletonItems(() => metrics.value, 4);

/** The change against the range before, as a StatCard trend; none when either range has no value. @internal */
function difference(
  current: number | null,
  previous: number | null | undefined,
  format: (value: number) => string,
): { trend?: StatCardTrend } {
  if (current === null || previous == null) return {};
  const change = current - previous;
  return {
    trend: {
      value: change,
      text: `${change > 0 ? "+" : change < 0 ? "−" : ""}${format(Math.abs(change))}`,
      label: priorLabel.value,
    },
  };
}
</script>

<template>
  <section aria-label="Deployment measures">
    <DataRegion
      :is-first-load="stats.isFirstLoad.value"
      :is-refreshing="stats.isRefreshing.value"
      :error="stats.error.value"
      :has-data="Boolean(stats.data.value)"
      loading-label="Loading deployment measures"
      @retry="stats.refetch()"
    >
      <!-- The SDK trend row is one inline-flex line whose readout and caption each wrap inside themselves, so a narrow
           phone tile split "−19 pts" and "vs prior 6 months" into two columns. Here each keeps its words together and
           the caption wraps whole under the readout; an fe sdk candidate. -->
      <div class="grid grid-cols-2 gap-3 xl:grid-cols-4">
        <StatCard
          v-for="(metric, index) in tiles"
          :key="metric?.label ?? `placeholder-${index}`"
          v-bind="metric ?? { label: '', value: '' }"
          size="sm"
          class="wh-glass min-w-0 rounded-2xl border border-(--wh-glass-edge) bg-(--wh-glass-fill) p-3 sm:p-4 [&>div>span.inline-flex]:flex-wrap [&>div>span.inline-flex]:gap-x-1 [&>div>span.inline-flex]:whitespace-nowrap"
        />
      </div>
    </DataRegion>
  </section>
</template>
