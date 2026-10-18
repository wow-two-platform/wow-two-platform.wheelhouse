<script lang="ts">
import type { ProductFacts } from "@/domain/portfolio";

/** A product's deploy trend: finished rollouts per week, dev and prod. */
export interface ProductTrendProps {
  readonly facts: ProductFacts | undefined;
  /** Each week's Monday; empty draws the placeholder while the summary loads. */
  readonly weeks: readonly string[];
  /** The weekly count a full column stands for, shared across the page so products compare. */
  readonly scale?: number | undefined;
}
</script>
<script setup lang="ts">
import { computed, inject } from "vue";
import { TrendScalesKey } from "@/application/portfolio/useProductFacts";
import WeeklyBars, { type WeeklySeries } from "./WeeklyBars.vue";

/**
 * Deploy frequency per environment over the summary's weeks, as stacked columns: prod from the baseline, dev above it,
 * each in its own validated hue. It answers "is this product moving, and does it reach prod" at a glance; the exact
 * counts sit in each column's title and the chart's label.
 */
defineOptions({ name: "ProductTrend" });
const props = defineProps<ProductTrendProps>();
defineSlots<{}>();
const scales = inject(TrendScalesKey, null);
const scale = computed(() => props.scale ?? scales?.value.products);
const Colors: Readonly<Record<string, string>> = {
  dev: "var(--color-chart-dev)",
  prod: "var(--color-chart-prod)",
};
const series = computed<WeeklySeries[]>(() =>
  [...(props.facts?.series ?? [])]
    .reverse()
    .map((item) => ({
      key: item.environment,
      label: `to ${item.environment}`,
      values: item.values,
      color: Colors[item.environment] ?? "var(--color-chart-3)",
    })),
);
const label = computed(() => {
  const totals = (props.facts?.series ?? []).map(
    (item) => `${item.values.reduce((sum, value) => sum + value, 0)} to ${item.environment}`,
  );
  return `Deploys over ${props.weeks.length} weeks: ${totals.join(", ") || "none"}`;
});
</script>
<template>
  <!-- A product never deployed has no trend to show. -->
  <span v-if="props.facts && props.weeks.length && !props.facts.lastDeployedAt" class="sr-only">Never deployed</span>
  <WeeklyBars v-else :weeks="props.facts ? props.weeks : []" :series="series" :label="label" :max="scale" />
</template>
