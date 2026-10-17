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
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { Tag } from "@wow-two-beta/fe/presentation/display";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";

import { EntitySelection, StatusFilter, type DeploymentFilterState as Filter } from "@/domain/deployments";

import DateRangeFilter from "./DateRangeFilter.vue";
import EntityPicker from "./EntityPicker.vue";

/**
 * One filter for both views and the measures: which workspaces, products and services, which outcomes, and when.
 * Picked entities show as chips that each remove themselves.
 */
defineOptions({ name: "DeploymentFilterBar" });
const props = withDefaults(defineProps<DeploymentFilterBarProps>(), { isEntityLocked: false });
const emit = defineEmits<{ change: [filter: Partial<Filter>] }>();

const statuses = [
  { value: StatusFilter.All, label: "All" },
  { value: StatusFilter.Succeeded, label: "Succeeded" },
  { value: StatusFilter.Failed, label: "Failed" },
] as const;
const chips = computed(() => (props.isEntityLocked ? [] : EntitySelection.chips(props.tree, props.filter.entities)));

/** Picks a status; pressing the active segment clears the group's value and keeps the status. @internal */
function setStatus(value: unknown): void {
  if (value === StatusFilter.All || value === StatusFilter.Succeeded || value === StatusFilter.Failed)
    emit("change", { status: value });
}

function remove(key: string): void {
  emit("change", { entities: props.filter.entities.filter((item) => item !== key) });
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-2">
    <div class="flex min-w-0 flex-wrap items-center gap-2">
      <EntityPicker
        v-if="!props.isEntityLocked"
        :tree="props.tree"
        :model-value="props.filter.entities"
        @update:model-value="emit('change', { entities: $event })"
      />
      <ToggleGroup
        variant="segmented"
        aria-label="Outcome"
        :model-value="props.filter.status"
        @update:model-value="setStatus"
      >
        <ToggleInput v-for="status in statuses" :key="status.value" :value="status.value" size="sm" class="min-h-11 md:min-h-0">{{
          status.label
        }}</ToggleInput>
      </ToggleGroup>
      <DateRangeFilter :model-value="props.filter.range" @update:model-value="emit('change', { range: $event })" />
    </div>
    <ul v-if="chips.length" class="flex flex-wrap gap-1.5" aria-label="Chosen products and services">
      <li v-for="chip in chips" :key="chip.key">
        <Tag :close-label="`Remove ${chip.label}`" @close="remove(chip.key)">{{ chip.label }}</Tag>
      </li>
    </ul>
  </div>
</template>
