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
  /** The prod targets a running fleet still holds; their rows can be neither chosen nor promoted alone. */
  readonly heldTargets?: ReadonlySet<string>;
}
</script>

<script setup lang="ts">
import { ReleaseSets } from "@/domain/deployments";
import { computed, ref, watch } from "vue";
import { RouterLink } from "vue-router";
import { ArrowRight, Layers, Rocket } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";
import { CheckboxInput } from "@wow-two-beta/fe/presentation/forms";

import { ConsolePaths } from "@/application/navigation";
import { FleetMaxItems, FleetRollouts, SelfDeployRules, rollbackRisks } from "@/domain/deployments";
import { InfoPopover, Panel } from "@/presentation/common/components";

import DevCheckPill from "../automation/DevCheckPill.vue";
import FrozenBadge from "../automation/FrozenBadge.vue";
import ReleaseChangelog from "../automation/ReleaseChangelog.vue";

/**
 * Offers each product's newest dev-proven release to prod: one at a time through the deploy dialog, or several — the
 * chosen rows or all of them — as one fleet rollout, which the fleet dialog confirms. A row a running fleet holds shows
 * so and stays out of both.
 */
defineOptions({ name: "ReadyToPromotePanel" });
const props = defineProps<ReadyToPromotePanelProps>();
const emit = defineEmits<{
  promote: [promotion: DeploymentPromotion];
  promoteMany: [promotions: DeploymentPromotion[]];
  retry: [];
}>();
const rows = useSkeletonItems(() => props.items, 2);

/** The chosen rows, by target and release; a row that leaves the list leaves the choice. */
const chosen = ref<ReadonlySet<string>>(new Set());
const held = computed(() => props.heldTargets ?? new Set<string>());
/** The rows a fleet may take: not held by a running one, and never Wheelhouse itself, which is promoted on its own. */
const free = computed(() =>
  (props.items ?? []).filter((item) => !held.value.has(item.prodTargetId) && !SelfDeployRules.isSelfProduct(item.product)),
);
const selection = computed(() => FleetRollouts.selection(props.items, chosen.value, held.value));
const allChosen = computed(() => free.value.length > 0 && selection.value.length === Math.min(free.value.length, FleetMaxItems));
watch(
  () => (props.items ?? []).map(FleetRollouts.key).join(","),
  () => {
    const keys = new Set((props.items ?? []).map(FleetRollouts.key));
    chosen.value = new Set([...chosen.value].filter((key) => keys.has(key)));
  },
);

function toggle(item: DeploymentPromotion, value: boolean): void {
  const next = new Set(chosen.value);
  if (value) next.add(FleetRollouts.key(item));
  else next.delete(FleetRollouts.key(item));
  chosen.value = next;
}

function toggleAll(value: boolean): void {
  chosen.value = new Set(value ? free.value.slice(0, FleetMaxItems).map(FleetRollouts.key) : []);
}

function name(item: DeploymentPromotion): string {
  return props.productNames.get(item.product) ?? item.product;
}
</script>

<template>
  <Panel title="Ready to promote">
    <template #info>
      <InfoPopover title="Ready to promote">
        <p>
          Per product, the newest release that succeeded on dev and is not what prod runs. Promote opens the deploy
          dialog on its prod target with that release; nothing deploys before you confirm there.
        </p>
        <p class="mt-2">
          Dev check is the release's end-to-end check, run once its dev rollout succeeds: passed, failed, not run yet, or
          no check declared. Auto-promote takes a release only once its check passed, or when it declares none; a
          failed or missing result holds it, and promoting by hand is still yours to decide.
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
          <div class="flex min-w-0 items-start gap-3">
          <label
            v-if="item && free.length > 1 && !SelfDeployRules.isSelfProduct(item.product)"
            class="-m-2 flex min-h-11 min-w-11 shrink-0 cursor-pointer items-center justify-center p-2 md:min-h-0 md:min-w-0"
          >
            <CheckboxInput
              :model-value="chosen.has(FleetRollouts.key(item))"
              :is-disabled="held.has(item.prodTargetId)"
              :aria-label="`Choose ${ReleaseSets.text(item.release)} of ${name(item)} for a fleet rollout`"
              @update:model-value="toggle(item, $event)"
            />
          </label>
          <div class="min-w-0">
            <p v-if="item" class="flex flex-wrap items-center gap-2">
              <RouterLink
                :to="ConsolePaths.product(item.product)"
                class="font-medium hover:text-primary focus-visible:outline-primary"
                >{{ props.productNames.get(item.product) ?? item.product }}</RouterLink
              >
              <FrozenBadge :product="item.product" environment="prod" />
              <Badge v-if="held.has(item.prodTargetId)" variant="info">
                <Layers :size="12" aria-hidden="true" class="mr-1 inline" />In a fleet rollout
              </Badge>
            </p>
            <p v-if="item" class="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted-foreground">
              <span class="font-mono text-xs text-foreground">{{ ReleaseSets.text(item.prodRelease) || "nothing" }}</span>
              <ArrowRight :size="14" aria-label="to" />
              <span class="font-mono text-xs font-medium text-foreground">{{ ReleaseSets.text(item.release) }}</span>
              <span>· succeeded on dev <RelativeTime :value="item.devSucceededAt" /></span>
            </p>
            <p v-if="item" class="mt-1.5 flex flex-wrap items-center gap-1.5">
              <DevCheckPill :verification="item.verification" :verifies="item.verifies" has-hold />
              <Badge v-for="risk in rollbackRisks(item)" :key="risk" variant="warning">{{ risk }}</Badge>
            </p>
            <ReleaseChangelog v-if="item" class="mt-2" :product="item.product" :from="item.prodRelease" :to="item.release" />
          </div>
          </div>
          <Button
            v-if="item"
            size="sm"
            class="max-md:min-h-11"
            :is-disabled="held.has(item.prodTargetId)"
            :aria-label="`Promote ${ReleaseSets.text(item.release)} of ${props.productNames.get(item.product) ?? item.product} to prod`"
            @click="emit('promote', item)"
          >
            <template #leading><Rocket :size="14" aria-hidden="true" /></template>Promote
          </Button>
        </li>
      </ul>
    </DataRegion>
    <template v-if="free.length > 1" #actions>
      <label class="flex min-h-11 cursor-pointer items-center gap-2 text-sm md:min-h-0">
        <CheckboxInput
          :model-value="allChosen"
          :is-indeterminate="selection.length > 0 && !allChosen"
          aria-label="Choose every release ready to promote"
          @update:model-value="toggleAll"
        />
        <span class="max-sm:sr-only">All</span>
      </label>
      <Button
        size="sm"
        variant="solid"
        tone="primary"
        class="max-md:min-h-11"
        :is-disabled="selection.length < 1 || held.size > 0"
        :title="held.size > 0 ? 'A fleet rollout is running' : undefined"
        @click="emit('promoteMany', selection)"
      >
        <template #leading><Layers :size="14" aria-hidden="true" /></template>
        {{ selection.length ? `Promote ${selection.length}` : "Promote selected" }}
      </Button>
      <Button
        size="sm"
        variant="outline"
        tone="neutral"
        class="max-md:min-h-11"
        :is-disabled="held.size > 0"
        :title="held.size > 0 ? 'A fleet rollout is running' : undefined"
        @click="emit('promoteMany', free.slice(0, FleetMaxItems))"
      >
        Promote all
      </Button>
    </template>
  </Panel>
</template>
