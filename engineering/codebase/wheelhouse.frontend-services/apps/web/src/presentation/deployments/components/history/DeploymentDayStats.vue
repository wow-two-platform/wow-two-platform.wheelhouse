<script lang="ts">
import type { ActivityDay, StatusFilter } from "@/domain/deployments";

/** Defines the chosen day's counts. */
export interface DeploymentDayStatsProps {
  /** The day's finished rollouts; undefined for a day with none. */
  readonly day: ActivityDay | undefined;
  /** The status filter: a filtered view counts its one outcome. */
  readonly status: StatusFilter;
  /** The day's live rollouts — only today has any. */
  readonly running: number;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { ActivityCalendar } from "@/domain/deployments";

/** The chosen day at a glance, in one compact row above its deployments: deployments, succeeded, failed, running. */
defineOptions({ name: "DeploymentDayStats" });
const props = defineProps<DeploymentDayStatsProps>();

const Tones: Record<string, string> = {
  succeeded: "text-success",
  failed: "text-destructive",
  running: "text-primary",
};
const stats = computed(() => ActivityCalendar.dayStats(props.day, props.status, props.running));
</script>

<template>
  <dl class="flex min-w-0 flex-wrap gap-2" aria-label="The day's counts">
    <div v-for="stat in stats" :key="stat.key" class="flex items-baseline gap-1.5 rounded-lg bg-muted/60 px-2.5 py-1">
      <dt class="order-2 text-xs text-muted-foreground">
        {{ stat.label.toLowerCase() }}
      </dt>
      <dd :class="['order-1 text-sm font-semibold tabular-nums', stat.value ? (Tones[stat.key] ?? '') : '']">
        {{ stat.value }}
      </dd>
    </div>
  </dl>
</template>
