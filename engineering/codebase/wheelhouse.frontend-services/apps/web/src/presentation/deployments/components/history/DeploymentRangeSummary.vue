<script lang="ts">
import type { ActivityProduct, RangeTotals, StatusFilter } from "@/domain/deployments";

/** Defines the calendar's range summary, shown beside it while no day is chosen. */
export interface DeploymentRangeSummaryProps {
  /** The range's name, such as `Last 6 months`. */
  readonly rangeLabel: string;
  /** The range's finished rollouts under the filter. */
  readonly totals: RangeTotals;
  /** The status filter: a filtered view counts only its outcome. */
  readonly status: StatusFilter;
  /** The filter's live rollouts, when the range reaches now. */
  readonly running: number;
  /** The busiest products, most first; empty hides the list. */
  readonly products: readonly ActivityProduct[];
  /** Product names by slug. */
  readonly productNames: ReadonlyMap<string, string>;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { RouterLink } from "vue-router";

import { ConsolePaths, ProductTab } from "@/application/navigation";

/**
 * The range at a glance while no day is chosen: deployments, how many succeeded and failed, what runs now, and the
 * busiest products, each a bar split by outcome that opens the product's deployments.
 */
defineOptions({ name: "DeploymentRangeSummary" });
const props = defineProps<DeploymentRangeSummaryProps>();

const isAll = computed(() => props.status === "all");
const tiles = computed(() => [
  { label: "Deployments", value: props.totals.succeeded + props.totals.failed + (isAll.value ? props.running : 0), tone: "" },
  ...(props.status !== "failed" ? [{ label: "Succeeded", value: props.totals.succeeded, tone: "text-success" }] : []),
  ...(props.status !== "succeeded" ? [{ label: "Failed", value: props.totals.failed, tone: "text-destructive" }] : []),
  ...(isAll.value ? [{ label: "Running", value: props.running, tone: props.running ? "text-primary" : "" }] : []),
]);
const busiest = computed(() => {
  const most = Math.max(1, ...props.products.map((item) => item.succeeded + item.failed));
  return props.products.map((item) => ({
    ...item,
    name: props.productNames.get(item.product) ?? item.product,
    total: item.succeeded + item.failed,
    succeededShare: (item.succeeded / most) * 100,
    failedShare: (item.failed / most) * 100,
  }));
});
</script>

<template>
  <section aria-labelledby="deployments-range-summary" class="flex min-w-0 flex-col gap-4">
    <div>
      <h3 id="deployments-range-summary" class="text-sm font-semibold">{{ props.rangeLabel }}</h3>
      <p class="mt-0.5 text-xs text-muted-foreground">Choose a day to see its deployments.</p>
    </div>
    <dl class="grid grid-cols-2 gap-2">
      <div v-for="tile in tiles" :key="tile.label" class="rounded-xl bg-muted/60 px-3 py-2">
        <dt class="text-xs text-muted-foreground">{{ tile.label }}</dt>
        <dd :class="['text-lg font-semibold tabular-nums', tile.tone]">{{ tile.value }}</dd>
      </div>
    </dl>
    <div v-if="busiest.length" class="min-w-0">
      <h4 class="text-xs font-medium text-muted-foreground">Busiest products</h4>
      <ol class="mt-2 flex flex-col gap-2">
        <li v-for="item in busiest" :key="item.product" class="min-w-0">
          <RouterLink
            :to="ConsolePaths.product(item.product, ProductTab.Deployments)"
            class="group block min-w-0 rounded-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
            :aria-label="`${item.name}: ${item.succeeded} succeeded, ${item.failed} failed`"
          >
            <span class="flex items-baseline justify-between gap-3 text-sm">
              <span class="truncate group-hover:text-primary">{{ item.name }}</span>
              <span class="shrink-0 font-mono text-xs tabular-nums text-muted-foreground"
                >{{ item.total }}<template v-if="item.failed"> · {{ item.failed }} failed</template></span
              >
            </span>
            <span class="mt-1 flex h-1.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
              <span class="h-full bg-success" :style="{ width: `${item.succeededShare}%` }" />
              <span class="h-full bg-destructive" :style="{ width: `${item.failedShare}%` }" />
            </span>
          </RouterLink>
        </li>
      </ol>
    </div>
  </section>
</template>
