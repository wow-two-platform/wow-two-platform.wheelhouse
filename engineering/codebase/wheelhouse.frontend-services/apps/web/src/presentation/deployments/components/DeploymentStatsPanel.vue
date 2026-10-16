<script lang="ts">
/** Defines the global deployment statistics panel. */
export interface DeploymentStatsPanelProps {
  /** Includes the per-target totals beneath the portfolio metrics. */
  readonly showTargets?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, useRoute } from "vue-router";

import {
  RelativeTime,
  Sparkline,
  StatCard,
  type StatCardTrend,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Text,
} from "@wow-two-beta/ui-vue/presentation/display";
import {
  DataRegion,
  SkeletonStateSlot,
  useSkeletonItems,
} from "@wow-two-beta/ui-vue/presentation/feedback";

import { useDeploymentStats } from "@/application/deployments";
import { Measures } from "@/domain/common";
import { Panel } from "@/presentation/common/components";

import JobStatusIndicator from "./JobStatusIndicator.vue";

/** Renders complete global deployment statistics, independently of the 50-row recent history window. */
defineOptions({ name: "DeploymentStatsPanel" });
const props = withDefaults(defineProps<DeploymentStatsPanelProps>(), {
  showTargets: false,
});
const stats = useDeploymentStats(30);
const route = useRoute();
const recent = computed(() => stats.data.value?.daily.slice(-14) ?? []);
const chartSeries = computed(() => [
  {
    title: "Finished deploys per day",
    tone: "brand" as const,
    data: recent.value.map((day) => day.succeeded + day.failed),
  },
  {
    title: "Failed rollouts per day",
    tone: "danger" as const,
    data: recent.value.map((day) => day.failed),
  },
]);
const metrics = computed(() => {
  const data = stats.data.value;
  if (!data) return [];
  const prior = data.previous;
  return [
    {
      label: "Deploys",
      value: data.deploys,
      helper: `${data.succeeded} succeeded · ${data.failed} failed · ${data.refused} refused`,
      ...difference(data.deploys, prior?.deploys, (value) => String(value)),
      isLowerBetter: false,
    },
    {
      label: "Success rate",
      value: Measures.percent(data.successRate),
      helper: "Of finished rollouts",
      ...difference(
        data.successRate,
        prior?.successRate,
        (value) => `${Math.round(value * 100)} pts`,
      ),
      isLowerBetter: false,
    },
    {
      label: "Median rollout",
      value: Measures.duration(data.medianRolloutSeconds),
      helper: "Start to verified",
      ...difference(
        data.medianRolloutSeconds,
        prior?.medianRolloutSeconds,
        Measures.duration,
      ),
      isLowerBetter: true,
    },
    {
      label: "Median recovery",
      value: Measures.duration(data.medianRecoverySeconds),
      helper: "Failure to next success",
      ...difference(
        data.medianRecoverySeconds,
        prior?.medianRecoverySeconds,
        Measures.duration,
      ),
      isLowerBetter: true,
    },
  ];
});

/** The metric tiles, or placeholder tiles until the first statistics arrive. */
const tiles = useSkeletonItems(() => (stats.data.value ? metrics.value : undefined), 4);
/** The per-target totals, or placeholder rows until the first statistics arrive. */
const targetRows = useSkeletonItems(() => stats.data.value?.targets, 3);

/** The change against the prior window as a StatCard trend; none when either window has no value. */
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
      label: "vs prior 30 days",
    },
  };
}
</script>

<template>
  <Panel
    title="Portfolio deployments · 30 days"
    description="All products and environments, from complete submission records."
  >
    <template #actions>
      <RouterLink
        class="text-sm text-primary hover:underline"
        :to="{ path: '/deployments', query: route.query }"
      >
        History
      </RouterLink>
    </template>
    <!-- The panel has no refresh control, so a failed first load offers its own retry. -->
    <DataRegion
      :is-first-load="stats.isFirstLoad.value"
      :is-refreshing="stats.isRefreshing.value"
      :error="stats.error.value"
      :has-data="Boolean(stats.data.value)"
      loading-label="Loading deployment statistics"
      @retry="stats.refetch()"
    >
      <div class="flex flex-col gap-5">
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            v-for="(metric, index) in tiles"
            :key="metric?.label ?? `placeholder-${index}`"
            v-bind="metric ?? { label: '', value: '' }"
            size="sm"
          />
        </div>
        <div class="grid gap-4 md:grid-cols-2">
          <div
            v-for="series in chartSeries"
            :key="series.title"
            class="rounded-xl border border-border p-4"
          >
            <div class="mb-3 flex items-baseline justify-between gap-3 text-sm">
              <Text as="span" class="text-sm">{{ series.title }}</Text>
              <SkeletonStateSlot class="font-medium tabular-nums">{{
                series.data.reduce((sum, value) => sum + value, 0)
              }}</SkeletonStateSlot>
            </div>
            <SkeletonStateSlot is-block shape="rect">
            <Sparkline
              :data="series.data"
              variant="bar"
              :tone="series.tone"
              :height="48"
              :min="0"
              :max="Math.max(1, ...series.data)"
              class="w-full overflow-hidden"
              :aria-label="`${series.title}: ${series.data.join(', ')}`"
            />
            </SkeletonStateSlot>
          </div>
        </div>
        <p v-if="recent[0]" class="text-xs text-muted-foreground">
          Last {{ recent.length }} UTC days, from {{ recent[0].date }}. Refusals
          changed no containers.
        </p>
        <Table
          v-if="props.showTargets && targetRows.length"
          density="compact"
          is-hoverable
        >
          <TableHead
            ><TableRow>
              <TableHeaderCell>Target</TableHeaderCell
              ><TableHeaderCell>Deploys</TableHeaderCell>
              <TableHeaderCell>Failed</TableHeaderCell
              ><TableHeaderCell>Last outcome</TableHeaderCell>
              <TableHeaderCell>When</TableHeaderCell>
            </TableRow></TableHead
          >
          <TableBody>
            <TableRow
              v-for="(target, index) in targetRows"
              :key="target?.targetId ?? `placeholder-${index}`"
            >
              <TableCell class="font-mono text-xs">{{
                target?.targetId
              }}</TableCell>
              <TableCell class="tabular-nums">{{ target?.deploys }}</TableCell>
              <TableCell class="tabular-nums">{{ target?.failed }}</TableCell>
              <!-- A placeholder row leaves composite cells empty, so each draws one bar. -->
              <TableCell>
                <span v-if="target" class="block min-w-0">
                  <JobStatusIndicator :status="target.lastStatus" />
                  <span
                    v-if="target.lastRelease"
                    class="mt-1 block font-mono text-xs"
                    >{{ target.lastRelease }}</span
                  >
                </span>
              </TableCell>
              <TableCell class="whitespace-nowrap text-xs text-muted-foreground"
                ><RelativeTime v-if="target" :value="target.lastDeployAt"
              /></TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </DataRegion>
  </Panel>
</template>
