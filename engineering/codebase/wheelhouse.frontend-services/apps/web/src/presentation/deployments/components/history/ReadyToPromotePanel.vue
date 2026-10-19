<script lang="ts">
import type { DeploymentPromotion } from "@/domain/deployments";

/** Defines the releases ready for prod. */
export interface ReadyToPromotePanelProps {
  /** Per product, the newest release proven on dev that prod does not run; undefined until the first read. */
  readonly items: readonly DeploymentPromotion[] | undefined;
  /** Product names by slug. */
  readonly productNames: ReadonlyMap<string, string>;
  readonly isFirstLoad: boolean;
  readonly error: { readonly message: string } | null;
}
</script>

<script setup lang="ts">
import { RouterLink } from "vue-router";
import { ArrowRight, Rocket } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";

import { ConsolePaths } from "@/application/navigation";
import { rollbackRisks } from "@/domain/deployments";
import { InfoPopover, Panel } from "@/presentation/common/components";

import FrozenBadge from "../automation/FrozenBadge.vue";
import ReleaseChangelog from "../automation/ReleaseChangelog.vue";

/** Offers each product's newest dev-proven release to prod in one action, which opens the deploy dialog on it. */
defineOptions({ name: "ReadyToPromotePanel" });
const props = defineProps<ReadyToPromotePanelProps>();
const emit = defineEmits<{ promote: [promotion: DeploymentPromotion]; retry: [] }>();
const rows = useSkeletonItems(() => props.items, 2);
</script>

<template>
  <Panel title="Ready to promote">
    <template #info>
      <InfoPopover title="Ready to promote">
        <p>
          Per product, the newest release that succeeded on dev and is not what prod runs. Promote opens the deploy
          dialog on its prod target with that release; nothing deploys before you confirm there.
        </p>
      </InfoPopover>
    </template>
    <DataRegion
      :is-first-load="props.isFirstLoad"
      :error="props.error"
      :has-data="props.items !== undefined"
      :is-empty="props.items !== undefined && !props.items.length"
      empty-title="Nothing waiting for prod"
      empty-description="Every product's prod runs its newest dev-proven release."
      loading-label="Loading releases ready for prod"
      @retry="emit('retry')"
    >
      <ul class="flex flex-col divide-y divide-border">
        <li
          v-for="(item, index) in rows"
          :key="item ? `${item.product}-${item.prodTargetId}` : `placeholder-${index}`"
          class="flex flex-wrap items-center justify-between gap-3 py-3"
        >
          <div class="min-w-0">
            <p v-if="item" class="flex flex-wrap items-center gap-2">
              <RouterLink
                :to="ConsolePaths.product(item.product)"
                class="font-medium hover:text-primary focus-visible:outline-primary"
                >{{ props.productNames.get(item.product) ?? item.product }}</RouterLink
              >
              <FrozenBadge :product="item.product" environment="prod" />
            </p>
            <p v-if="item" class="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted-foreground">
              <span class="font-mono text-xs text-foreground">{{ item.prodRelease ?? "nothing" }}</span>
              <ArrowRight :size="14" aria-label="to" />
              <span class="font-mono text-xs font-medium text-foreground">{{ item.release }}</span>
              <span>· passed dev <RelativeTime :value="item.devSucceededAt" /></span>
            </p>
            <p v-if="item && rollbackRisks(item).length" class="mt-1 flex flex-wrap gap-1.5">
              <Badge v-for="risk in rollbackRisks(item)" :key="risk" variant="warning">{{ risk }}</Badge>
            </p>
            <ReleaseChangelog v-if="item" class="mt-2" :product="item.product" :from="item.prodRelease" :to="item.release" />
          </div>
          <Button
            v-if="item"
            size="sm"
            class="max-md:min-h-11"
            :aria-label="`Promote ${item.release} of ${props.productNames.get(item.product) ?? item.product} to prod`"
            @click="emit('promote', item)"
          >
            <template #leading><Rocket :size="14" aria-hidden="true" /></template>Promote
          </Button>
        </li>
      </ul>
    </DataRegion>
  </Panel>
</template>
