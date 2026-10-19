<script lang="ts">
import type { FleetRollout } from "@/domain/deployments";

/** Defines the recent fleet rollouts. */
export interface FleetRolloutsPanelProps {
  /** The fleets that ended lately, newest first; the panel is left out while there are none. */
  readonly items: readonly FleetRollout[];
  /** Product names by slug. */
  readonly productNames: ReadonlyMap<string, string>;
}
</script>

<script setup lang="ts">
import { FleetRollouts } from "@/domain/deployments";
import { Panel } from "@/presentation/common/components";

import FleetRolloutCard from "../automation/FleetRolloutCard.vue";

/** Lists the fleet rollouts that ended lately, newest open, each with every product's outcome. */
defineOptions({ name: "FleetRolloutsPanel" });
const props = defineProps<FleetRolloutsPanelProps>();
</script>

<template>
  <Panel title="Fleet rollouts">
    <ul class="flex flex-col divide-y divide-border">
      <li v-for="(fleet, index) in props.items" :key="fleet.id" class="py-3 first:pt-0 last:pb-0">
        <details :open="index === 0">
          <summary class="cursor-pointer py-1 text-sm font-medium">{{ FleetRollouts.summary(fleet) }}</summary>
          <FleetRolloutCard class="mt-2" :fleet="fleet" :product-names="props.productNames" />
        </details>
      </li>
    </ul>
  </Panel>
</template>
