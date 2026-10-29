<script lang="ts">
import type { DeploymentItem, FleetRollout } from "@/domain/deployments";

/** Defines the live rollouts strip. */
export interface RunningNowPanelProps {
  /** The live rollouts; the panel is left out while there are none and no fleet runs. */
  readonly items: readonly DeploymentItem[];
  /** The fleet rollout that runs, shown above the live rollouts with its progress. */
  readonly fleet?: FleetRollout | null;
  /** Whether a cancel request for the fleet is on its way. */
  readonly cancellingFleet?: boolean;
  /** Product names by slug. */
  readonly productNames: ReadonlyMap<string, string>;
}
</script>

<script setup lang="ts">
import { ReleaseSets } from "@/domain/deployments";
import { pluralize } from "@wow-two-beta/fe/foundation/formatters";
import { ServiceControls } from "@/domain/deployments/controls";
import { onBeforeUnmount, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import { LoaderCircle } from "lucide-vue-next";

import { Badge } from "@wow-two-beta/fe/presentation/display";

import { ConsolePaths } from "@/application/navigation";
import { Measures } from "@/domain/common";
import { Panel } from "@/presentation/common/components";

import FleetRolloutCard from "../automation/FleetRolloutCard.vue";
import FrozenBadge from "../automation/FrozenBadge.vue";

/** Shows each live rollout with the step it is on and how long it has run; the time ticks every second. */
defineOptions({ name: "RunningNowPanel" });
const props = defineProps<RunningNowPanelProps>();
const emit = defineEmits<{ cancelFleet: [id: string] }>();
const now = ref(Date.now());
let timer: ReturnType<typeof setInterval> | undefined;

onMounted(() => {
  timer = setInterval(() => (now.value = Date.now()), 1000);
});
onBeforeUnmount(() => clearInterval(timer));

function elapsed(item: DeploymentItem): string {
  const since = Date.parse(item.startedAt ?? item.submittedAt);
  return Number.isFinite(since) ? Measures.duration(Math.max(0, (now.value - since) / 1000)) : "—";
}
</script>

<template>
  <Panel title="Running now" :description="`${props.items.length} ${pluralize(props.items.length, 'rollout')} in progress.`">
    <FleetRolloutCard
      v-if="props.fleet"
      :fleet="props.fleet"
      :product-names="props.productNames"
      :cancelling="props.cancellingFleet"
      class="mb-4 border-b border-border pb-4"
      aria-live="polite"
      @cancel="emit('cancelFleet', $event)"
    />
    <ul class="flex flex-col divide-y divide-border" aria-live="polite">
      <li v-for="item in props.items" :key="item.id">
        <RouterLink
          :to="`${ConsolePaths.deployments}/${encodeURIComponent(item.id)}`"
          class="grid min-h-11 grid-cols-[1.25rem_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 py-3 hover:text-primary focus-visible:outline-primary"
        >
          <LoaderCircle :size="18" class="text-primary motion-safe:animate-spin" aria-hidden="true" />
          <span class="min-w-0">
            <span class="flex flex-wrap items-center gap-2">
              <span class="font-medium">{{ item.product ? (props.productNames.get(item.product) ?? item.product) : item.targetId }}</span>
              <Badge :variant="item.environment === 'prod' ? 'info' : 'neutral'">{{ item.environment ?? item.targetId }}</Badge>
              <span class="font-mono text-xs">{{
                item.operation ? ServiceControls.labelOf(item.operation) : ReleaseSets.text(item.release) || item.bundleId
              }}</span>
              <FrozenBadge :product="item.product" :environment="item.environment" />
            </span>
            <span class="mt-0.5 block truncate text-sm text-muted-foreground">
              <template v-if="item.currentStep">
                {{ item.currentStep.name }}<template v-if="item.currentStep.index"> · step {{ item.currentStep.index }}</template>
              </template>
              <template v-else>{{ item.status === "submitting" ? "Submitting" : "Queued on the target" }}</template>
            </span>
          </span>
          <span class="font-mono text-sm tabular-nums" :aria-label="`Running for ${elapsed(item)}`">{{ elapsed(item) }}</span>
        </RouterLink>
      </li>
    </ul>
  </Panel>
</template>
