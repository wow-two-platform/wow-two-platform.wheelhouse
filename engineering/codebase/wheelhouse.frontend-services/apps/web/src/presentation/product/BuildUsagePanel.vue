<script lang="ts">
/** Defines a product's build usage panel. */
export interface BuildUsagePanelProps {
  /** The product's slug. */
  readonly product: string;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { Hammer } from "lucide-vue-next";

import { DataRegion, SkeletonStateSlot } from "@wow-two-beta/fe/presentation/feedback";

import { useBuildUsage } from "@/application/deployments";
import { InfoPopover } from "@/presentation/common/components";

/**
 * What a product's builds cost: its GitHub Actions minutes over the last 30 days and the average per run, from the
 * product's usage read (`GET /api/deployments/products/{product}/usage`, read from GitHub at most once an hour). Where
 * the read was cut short, the figures are a floor and say so.
 */
defineOptions({ name: "BuildUsagePanel" });
const props = defineProps<BuildUsagePanelProps>();
defineSlots<{}>();

const usage = useBuildUsage(() => props.product);
const actions = computed(() => usage.data.value?.actions ?? null);
const floor = computed(() => (actions.value && !actions.value.complete ? "≥ " : ""));
const minutes = computed(() => (actions.value ? `${floor.value}${Math.round(actions.value.minutes).toLocaleString()}` : "0"));
const average = computed(() => {
  const value = actions.value;
  if (!value || !value.runs) return null;
  const perRun = value.minutes / value.runs;
  return perRun < 10 ? perRun.toFixed(1) : Math.round(perRun).toString();
});
const runs = computed(() => actions.value?.runs ?? 0);
const days = computed(() => usage.data.value?.days ?? 30);
</script>

<template>
  <section class="wh-glass rounded-2xl border border-border p-5" aria-labelledby="build-usage-heading" data-info-scope>
    <div class="mb-3 flex items-center gap-1">
      <h3 id="build-usage-heading" class="flex items-center gap-2 text-sm font-semibold">
        <Hammer :size="16" aria-hidden="true" />Builds
      </h3>
      <InfoPopover title="Builds">
        <p>
          The wall-clock minutes of this product's finished GitHub Actions runs over the last {{ days }} days, and the
          average per run. A public repository's minutes are free. Read from GitHub at most once an hour.
        </p>
        <p class="mt-2">A ≥ marks a floor: the product ran more workflows than one read lists.</p>
      </InfoPopover>
    </div>
    <DataRegion
      :is-first-load="usage.isFirstLoad.value"
      :error="usage.error.value"
      :has-data="Boolean(usage.data.value)"
      loading-label="Reading build usage"
      @retry="usage.refetch()"
    >
      <dl class="grid grid-cols-2 gap-3">
        <div class="min-w-0 rounded-xl border border-border bg-card px-4 py-3">
          <dt class="text-xs text-muted-foreground">Actions minutes · {{ days }} days</dt>
          <dd class="mt-1 text-xl font-semibold tabular-nums">
            <SkeletonStateSlot>{{ minutes }}</SkeletonStateSlot>
          </dd>
        </div>
        <div class="min-w-0 rounded-xl border border-border bg-card px-4 py-3">
          <dt class="text-xs text-muted-foreground">Average per run</dt>
          <dd class="mt-1 flex items-baseline gap-1.5">
            <span class="text-xl font-semibold tabular-nums"
              ><SkeletonStateSlot>{{ average ?? "—" }}</SkeletonStateSlot></span
            ><span v-if="average" class="text-xs text-muted-foreground">min</span>
            <span class="ms-auto truncate text-xs text-muted-foreground tabular-nums"
              >{{ floor }}{{ runs }} {{ runs === 1 ? "run" : "runs" }}</span
            >
          </dd>
        </div>
      </dl>
    </DataRegion>
  </section>
</template>
