<script lang="ts">
import type { TrendPoint } from "@/domain/servers";

/** Defines one host figure's stored readings over a time window. */
export interface TrendLineProps {
  readonly label: string;
  /** Oldest first. */
  readonly points: readonly TrendPoint[];
  /** The window's start, so a short history sits at the right end instead of stretching. */
  readonly since: number;
  readonly until: number;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { Text } from "@wow-two-beta/fe/presentation/display";

import { USAGE_THRESHOLDS } from "@/domain/servers";

/** Draws a percentage over time with the warning and danger levels the meters use; no chart library needed. */
defineOptions({ name: "TrendLine" });
const props = defineProps<TrendLineProps>();

const Width = 240;
const Height = 48;
const ceiling = computed(() => Math.max(100, ...props.points.map((point) => point.value)));
const x = (at: number) =>
  ((at - props.since) / Math.max(1, props.until - props.since)) * Width;
const y = (value: number) => Height - (value / ceiling.value) * Height;
const line = computed(() =>
  props.points.map((point) => `${x(point.at).toFixed(1)},${y(point.value).toFixed(1)}`).join(" "),
);
const peak = computed(() => Math.max(...props.points.map((point) => point.value)));
const latest = computed(() => props.points.at(-1)?.value ?? null);
const summary = computed(() =>
  props.points.length < 2
    ? `${props.label}: not enough readings yet`
    : `${props.label}: now ${Math.round(latest.value!)}%, peak ${Math.round(peak.value)}%`,
);
</script>

<template>
  <div class="min-w-0 rounded-xl border border-border bg-card p-3">
    <div class="mb-2 flex items-center justify-between gap-3 text-xs">
      <Text as="span" class="truncate text-xs text-muted-foreground">{{ props.label }}</Text>
      <Text v-if="props.points.length >= 2" as="span" class="text-xs font-medium tabular-nums"
        >peak {{ Math.round(peak) }}%</Text
      >
    </div>
    <svg
      v-if="props.points.length >= 2"
      :viewBox="`0 0 ${Width} ${Height}`"
      preserveAspectRatio="none"
      class="h-12 w-full overflow-visible"
      role="img"
      :aria-label="summary"
    >
      <line
        v-for="level in USAGE_THRESHOLDS"
        :key="level"
        x1="0"
        :x2="Width"
        :y1="y(level)"
        :y2="y(level)"
        :stroke="level === USAGE_THRESHOLDS[1] ? 'var(--color-destructive)' : 'var(--color-warning)'"
        stroke-dasharray="3 3"
        stroke-width="0.75"
        opacity="0.5"
        vector-effect="non-scaling-stroke"
      />
      <polyline
        :points="line"
        fill="none"
        stroke="var(--color-primary)"
        stroke-width="1.5"
        stroke-linejoin="round"
        vector-effect="non-scaling-stroke"
      />
    </svg>
    <div v-else class="flex h-12 items-center">
      <Text as="span" class="text-xs text-muted-foreground">Not enough readings yet</Text>
    </div>
  </div>
</template>
