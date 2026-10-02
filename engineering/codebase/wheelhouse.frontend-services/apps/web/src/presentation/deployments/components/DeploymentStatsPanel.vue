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
  Sparkline,
  StatCard,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@wow-two-beta/ui-vue/presentation/display";
import { SkeletonState } from "@wow-two-beta/ui-vue/presentation/feedback";

import { useRefresh } from "@/bootstrap/query";
import { useDeploymentStats } from "@/application/deployments";
import { Measures } from "@/domain/common";
import { SkeletonStateSlot } from "@wow-two-beta/ui-vue/presentation/feedback";
import { LoadState, Panel } from "@/presentation/common/components";

import JobStatusIndicator from "./JobStatusIndicator.vue";

/** Renders complete global deployment statistics, independently of the 50-row recent history window. */
defineOptions({ name: "DeploymentStatsPanel" });
const props = withDefaults(defineProps<DeploymentStatsPanelProps>(), {
  showTargets: false,
});
const stats = useDeploymentStats(30);
const statsRefresh = useRefresh(stats.refetch);
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
      change: difference(data.deploys, prior?.deploys, (value) =>
        String(value),
      ),
    },
    {
      label: "Success rate",
      value: Measures.percent(data.successRate),
      helper: "Of finished rollouts",
      change: difference(
        data.successRate,
        prior?.successRate,
        (value) => `${Math.round(value * 100)} pts`,
      ),
    },
    {
      label: "Median rollout",
      value: Measures.duration(data.medianRolloutSeconds),
      helper: "Start to verified",
      change: difference(
        data.medianRolloutSeconds,
        prior?.medianRolloutSeconds,
        Measures.duration,
        true,
      ),
    },
    {
      label: "Median recovery",
      value: Measures.duration(data.medianRecoverySeconds),
      helper: "Failure to next success",
      change: difference(
        data.medianRecoverySeconds,
        prior?.medianRecoverySeconds,
        Measures.duration,
        true,
      ),
    },
  ];
});

/** Formats a real prior-window comparison, preserving unavailable values. */
function difference(
  current: number | null,
  previous: number | null | undefined,
  format: (value: number) => string,
  inverse = false,
) {
  if (current === null || previous == null) return null;
  const change = current - previous;
  const improved = inverse ? change < 0 : change > 0;
  return {
    text: `${change > 0 ? "+" : change < 0 ? "−" : ""}${format(Math.abs(change))}`,
    tone:
      change === 0
        ? "text-muted-foreground"
        : improved
          ? "text-success"
          : "text-destructive",
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
    <LoadState
      :loading="stats.loading.value && !stats.data.value"
      :refreshing="statsRefresh.refreshing.value"
      :error="stats.error.value"
      :has-data="Boolean(stats.data.value)"
      :empty="!stats.data.value"
      empty-title="No deployment records"
      @retry="statsRefresh.refresh()"
    >
      <template #skeleton>
        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <SkeletonState
            v-for="label in [
              'Deploys',
              'Success rate',
              'Median rollout',
              'Median recovery',
            ]"
            :key="label"
            class="h-32 rounded-xl"
          />
        </div>
      </template>
      <div v-if="stats.data.value" class="flex flex-col gap-5">
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            v-for="metric in metrics"
            :key="metric.label"
            :label="metric.label"
            :value="metric.value"
            size="sm"
          >
            <template #helper>
              <p>{{ metric.helper }}</p>
              <p v-if="metric.change" class="mt-2" :class="metric.change.tone">
                <SkeletonStateSlot>{{ metric.change.text }}</SkeletonStateSlot>
                <span class="text-muted-foreground">vs prior 30 days</span>
              </p>
            </template>
          </StatCard>
        </div>
        <div class="grid gap-4 md:grid-cols-2">
          <div
            v-for="series in chartSeries"
            :key="series.title"
            class="rounded-xl border border-border p-4"
          >
            <div class="mb-3 flex items-baseline justify-between gap-3 text-sm">
              <span>{{ series.title }}</span>
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
          v-if="props.showTargets && stats.data.value.targets.length"
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
              v-for="target in stats.data.value.targets"
              :key="target.targetId"
            >
              <TableCell class="font-mono text-xs">{{
                target.targetId
              }}</TableCell>
              <TableCell class="tabular-nums">{{ target.deploys }}</TableCell>
              <TableCell class="tabular-nums">{{ target.failed }}</TableCell>
              <TableCell>
                <JobStatusIndicator :status="target.lastStatus" />
                <span
                  v-if="target.lastRelease"
                  class="mt-1 block font-mono text-xs"
                  >{{ target.lastRelease }}</span
                >
              </TableCell>
              <TableCell
                class="whitespace-nowrap text-xs text-muted-foreground"
                :title="new Date(target.lastDeployAt).toLocaleString()"
                >{{ Measures.moment(target.lastDeployAt) }}</TableCell
              >
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </LoadState>
  </Panel>
</template>
