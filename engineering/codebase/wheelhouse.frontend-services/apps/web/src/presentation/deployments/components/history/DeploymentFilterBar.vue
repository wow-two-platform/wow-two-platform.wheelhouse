<script lang="ts">
import type { DeploymentFilterState, EntityTree } from "@/domain/deployments";

/** Defines the shared filter bar of a deployments view. */
export interface DeploymentFilterBarProps {
  /** The filter in force. */
  readonly filter: DeploymentFilterState;
  /** What the entity picker offers. */
  readonly tree: EntityTree;
  /** Hides the entity picker when the view belongs to one product. */
  readonly isEntityLocked?: boolean;
  /** Keeps the controls on one line on a phone, scrolling sideways, as a strip that sticks while the page scrolls. */
  readonly isSingleRow?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { Tag } from "@wow-two-beta/fe/presentation/display";

import { useDeploymentRangeStats } from "@/application/deployments";
import { EntitySelection, type DeploymentFilterState as Filter } from "@/domain/deployments";

import AttemptsToggle from "./AttemptsToggle.vue";
import DateRangeFilter from "./DateRangeFilter.vue";
import EntityPicker from "./EntityPicker.vue";

/**
 * One filter for both views and the measures: which workspaces, products and services, which attempts, and when.
 * Picked entities show as chips that each remove themselves. The All attempts toggle between the products and the range
 * is the viewer's own choice, remembered by this browser rather than kept in the address; its badge counts the attempts
 * hidden under the filter, from the measures' read, which it shares with the tiles. A product's own bar — the toggle and
 * the range — keeps one line on a phone: the toggle folds to its icon and badge, and the range's label truncates.
 */
defineOptions({ name: "DeploymentFilterBar" });
const props = withDefaults(defineProps<DeploymentFilterBarProps>(), { isEntityLocked: false, isSingleRow: false });
const emit = defineEmits<{ change: [filter: Partial<Filter>] }>();

const stats = useDeploymentRangeStats(() => props.filter);
/** The attempts a latest-of-day view hides under the filter: replaced that day, or changed nothing. */
const hidden = computed(() => {
  const current = stats.data.value?.current;
  return current ? current.notApplied + current.superseded : null;
});
const chips = computed(() => (props.isEntityLocked ? [] : EntitySelection.chips(props.tree, props.filter.entities)));

function remove(key: string): void {
  emit("change", { entities: props.filter.entities.filter((item) => item !== key) });
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-2">
    <!-- A phone's single-row strip scrolls sideways; its trailing edge fades, so controls past it read as more. Both
         strips keep 8px under their content, so a scrollbar thumb never sits against a chip. -->
    <div
      :class="[
        'flex min-w-0 flex-wrap items-center gap-2',
        props.isSingleRow
          ? 'max-md:-mx-1 max-md:flex-nowrap max-md:overflow-x-auto max-md:px-1 max-md:pr-6 max-md:pb-2 max-md:*:shrink-0 max-md:[mask-image:linear-gradient(to_right,#000_calc(100%-1.5rem),transparent)]'
          : props.isEntityLocked
            ? 'max-md:flex-nowrap'
            : '',
      ]"
    >
      <EntityPicker
        v-if="!props.isEntityLocked"
        :tree="props.tree"
        :model-value="props.filter.entities"
        @update:model-value="emit('change', { entities: $event })"
      />
      <AttemptsToggle :hidden="hidden" />
      <DateRangeFilter :model-value="props.filter.range" @update:model-value="emit('change', { range: $event })" />
    </div>
    <ul
      v-if="chips.length"
      :class="['flex gap-1.5', props.isSingleRow ? 'flex-wrap max-md:flex-nowrap max-md:overflow-x-auto max-md:pb-2 max-md:*:shrink-0' : 'flex-wrap']"
      aria-label="Chosen products and services"
    >
      <li v-for="chip in chips" :key="chip.key">
        <Tag :close-label="`Remove ${chip.label}`" @close="remove(chip.key)">{{ chip.label }}</Tag>
      </li>
    </ul>
  </div>
</template>
