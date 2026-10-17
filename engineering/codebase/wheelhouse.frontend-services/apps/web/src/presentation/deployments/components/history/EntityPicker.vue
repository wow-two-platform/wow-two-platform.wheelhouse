<script lang="ts">
import type { EntityTree } from "@/domain/deployments";

/** Defines the entity picker: the tree to pick from and the picked keys. */
export interface EntityPickerProps {
  /** Workspaces, their products and each product's services. */
  readonly tree: EntityTree;
  /** The picked keys: `w:` workspace, `p:` product, `s:` product/service. */
  readonly modelValue: readonly string[];
}
</script>

<script setup lang="ts">
import { computed, ref } from "vue";
import { ChevronDown, Layers } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { CountBadge } from "@wow-two-beta/fe/presentation/display";
import { BottomSheet, BottomSheetTitle, Popover, PopoverContent, PopoverTrigger } from "@wow-two-beta/fe/presentation/overlays";

import { EntitySelection } from "@/domain/deployments";

import { useIsPhone } from "../../hooks/useIsPhone";
import EntityTreeList from "./EntityTreeList.vue";

/**
 * One dropdown over a Workspace → Product → Service tree with mix-and-match checkboxes: a whole workspace, a product,
 * or single services, in any combination. A popover from `md` up; a bottom sheet on a phone.
 */
defineOptions({ name: "EntityPicker" });
const props = defineProps<EntityPickerProps>();
const emit = defineEmits<{ "update:modelValue": [value: string[]] }>();
const open = ref(false);
const isPhone = useIsPhone();

const count = computed(() => props.modelValue.length);
const summary = computed(() => {
  const chips = EntitySelection.chips(props.tree, props.modelValue);
  if (!chips.length) return "All products";
  return chips.length === 1 ? chips[0]!.label : `${chips.length} selected`;
});
</script>

<template>
  <template v-if="isPhone">
    <Button
      variant="outline"
      tone="neutral"
      size="sm"
      class="min-h-11 max-w-full justify-start"
      :aria-label="`Products and services: ${summary}`"
      aria-haspopup="dialog"
      @click="open = true"
    >
      <template #leading><Layers :size="16" aria-hidden="true" /></template>
      <span class="truncate">{{ summary }}</span>
      <CountBadge v-if="count > 1" :value="count" variant="neutral" class="ml-1" />
      <ChevronDown :size="14" class="ml-1 shrink-0 text-muted-foreground" aria-hidden="true" />
    </Button>
    <BottomSheet v-model:open="open" :snap-points="['70vh', '92vh']">
      <BottomSheetTitle class="px-1 pb-2 text-base font-semibold">Products and services</BottomSheetTitle>
      <EntityTreeList :tree="props.tree" :model-value="props.modelValue" @update:model-value="emit('update:modelValue', $event)" />
      <Button class="mt-3 min-h-11 w-full" @click="open = false">Done</Button>
    </BottomSheet>
  </template>
  <Popover v-else v-model:open="open" placement="bottom-start">
    <PopoverTrigger as-child>
      <Button
        variant="outline"
        tone="neutral"
        size="sm"
        class="min-h-9 max-w-full justify-start"
        :aria-label="`Products and services: ${summary}`"
      >
        <template #leading><Layers :size="16" aria-hidden="true" /></template>
        <span class="truncate">{{ summary }}</span>
        <CountBadge v-if="count > 1" :value="count" variant="neutral" class="ml-1" />
        <ChevronDown :size="14" class="ml-1 shrink-0 text-muted-foreground" aria-hidden="true" />
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-[min(22rem,calc(100vw-2rem))] p-0 text-sm" aria-label="Choose products and services">
      <EntityTreeList :tree="props.tree" :model-value="props.modelValue" @update:model-value="emit('update:modelValue', $event)" />
    </PopoverContent>
  </Popover>
</template>
