<script lang="ts">
import type { DeploymentItem } from "@/domain/deployments";

/** Defines the chosen day's counts. */
export interface DeploymentDayStatsProps {
  /** The day's deployments under the filter; undefined until the first read, which draws placeholder tiles. */
  readonly items: readonly DeploymentItem[] | undefined;
  /** Whether older deployments of the day remain unread, so the counts cover only those read. */
  readonly hasMore?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { Rocket } from "lucide-vue-next";

import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";

import { DeploymentStatusRules } from "@/domain/deployments";
import { statusClass, StatusIcons, statusIconClass } from "@/presentation/common/components";

/**
 * The chosen day at a glance, above its deployments: a compact tile for the day's total, then one per status that
 * occurred — succeeded, failed, degraded, interrupted, deploying, refused — each its icon, count and word in the same
 * tones as the status pills; a status nobody reached takes no tile. Tiles wrap, so a narrow day panel keeps every one.
 */
defineOptions({ name: "DeploymentDayStats" });
const props = withDefaults(defineProps<DeploymentDayStatsProps>(), { hasMore: false });
defineSlots<{}>();

const counts = computed(() => (props.items ? DeploymentStatusRules.counts(props.items) : []));
const total = computed(() => props.items?.length ?? 0);
const totalLabel = computed(() => `${total.value}${props.hasMore ? "+" : ""}`);
</script>

<template>
  <ul v-if="!props.items" class="flex min-w-0 flex-wrap gap-1.5" aria-hidden="true">
    <li v-for="index in 3" :key="index"><SkeletonState class="h-8 w-24 rounded-lg" /></li>
  </ul>
  <ul v-else-if="total" class="flex min-w-0 flex-wrap gap-1.5" aria-label="The day's deployments by status">
    <li
      class="inline-flex h-8 items-center gap-1.5 rounded-lg border border-border bg-muted/50 px-2.5 text-xs"
      :title="props.hasMore ? 'Load older deployments to count the rest' : undefined"
    >
      <Rocket :size="14" aria-hidden="true" class="shrink-0 text-muted-foreground" />
      <span class="text-sm font-semibold tabular-nums">{{ totalLabel }}</span>
      <span class="text-muted-foreground">{{ total === 1 && !props.hasMore ? "deployment" : "deployments" }}</span>
    </li>
    <li
      v-for="count in counts"
      :key="count.state"
      class="inline-flex h-8 items-center gap-1.5 rounded-lg border px-2.5 text-xs"
      :class="statusClass(count.state)"
    >
      <component
        :is="StatusIcons[count.state]"
        :size="14"
        aria-hidden="true"
        class="shrink-0"
        :class="statusIconClass(count.state)"
      />
      <span class="text-sm font-semibold tabular-nums">{{ count.count }}</span>
      <span data-detail class="text-muted-foreground">{{ count.word }}</span>
    </li>
  </ul>
</template>
