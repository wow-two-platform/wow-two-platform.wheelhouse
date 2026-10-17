<script lang="ts">
import type { DeploymentRange } from "@/domain/deployments";

/** Defines the date-time range control. */
export interface DateRangeFilterProps {
  /** The range in force. */
  readonly modelValue: DeploymentRange;
}
</script>

<script setup lang="ts">
import { computed, ref } from "vue";
import { CalendarRange, ChevronDown } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { BottomSheet, BottomSheetTitle, Popover, PopoverContent, PopoverTrigger } from "@wow-two-beta/fe/presentation/overlays";

import { DeploymentFilters, type DeploymentRange as Range } from "@/domain/deployments";

import { useIsPhone } from "../../hooks/useIsPhone";
import DateRangeChoices from "./DateRangeChoices.vue";

/** The range control: presets ending now or a custom from and to with time. A popover from `md` up; a bottom sheet on a phone. */
defineOptions({ name: "DateRangeFilter" });
const props = defineProps<DateRangeFilterProps>();
const emit = defineEmits<{ "update:modelValue": [value: Range] }>();
const open = ref(false);
const isPhone = useIsPhone();
const label = computed(() => DeploymentFilters.label(props.modelValue));

function choose(value: Range): void {
  emit("update:modelValue", value);
  open.value = false;
}
</script>

<template>
  <template v-if="isPhone">
    <Button
      variant="outline"
      tone="neutral"
      size="sm"
      class="min-h-11 max-w-full justify-start"
      :aria-label="`Date range: ${label}`"
      aria-haspopup="dialog"
      @click="open = true"
    >
      <template #leading><CalendarRange :size="16" aria-hidden="true" /></template>
      <span class="truncate">{{ label }}</span>
      <ChevronDown :size="14" class="ml-1 shrink-0 text-muted-foreground" aria-hidden="true" />
    </Button>
    <BottomSheet v-model:open="open" :snap-points="['80vh']">
      <BottomSheetTitle class="px-1 pb-1 text-base font-semibold">Date range</BottomSheetTitle>
      <DateRangeChoices v-if="open" :model-value="props.modelValue" @choose="choose" />
    </BottomSheet>
  </template>
  <Popover v-else v-model:open="open" placement="bottom-start">
    <PopoverTrigger as-child>
      <Button variant="outline" tone="neutral" size="sm" class="min-h-9 max-w-full justify-start" :aria-label="`Date range: ${label}`">
        <template #leading><CalendarRange :size="16" aria-hidden="true" /></template>
        <span class="truncate">{{ label }}</span>
        <ChevronDown :size="14" class="ml-1 shrink-0 text-muted-foreground" aria-hidden="true" />
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-[min(20rem,calc(100vw-2rem))] p-0 text-sm" aria-label="Choose a date range">
      <DateRangeChoices v-if="open" :model-value="props.modelValue" @choose="choose" />
    </PopoverContent>
  </Popover>
</template>
