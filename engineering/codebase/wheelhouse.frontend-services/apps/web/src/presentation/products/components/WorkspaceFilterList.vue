<script lang="ts">
/** One workspace the filter offers. */
export interface WorkspaceOption {
  readonly slug: string;
  readonly name: string;
  readonly count: number;
  /** A product in it is failing: the list marks it red. */
  readonly isFailing: boolean;
}

/** The workspace filter's checkbox list, shared by its popover and its phone sheet. */
export interface WorkspaceFilterListProps {
  readonly options: readonly WorkspaceOption[];
  /** The chosen slugs; empty for every workspace. */
  readonly modelValue: readonly string[];
}
</script>
<script setup lang="ts">
import { CheckboxInput } from "@wow-two-beta/fe/presentation/forms";

/**
 * Every workspace with its product count and a checkbox; none checked means every workspace. A workspace with a
 * failing product carries a red mark and the word, so it is found before it is opened.
 */
defineOptions({ name: "WorkspaceFilterList" });
const props = defineProps<WorkspaceFilterListProps>();
const emit = defineEmits<{ "update:modelValue": [value: string[]] }>();
defineSlots<{}>();

/** Adds or removes one workspace. @internal */
function toggle(slug: string): void {
  emit(
    "update:modelValue",
    props.modelValue.includes(slug) ? props.modelValue.filter((item) => item !== slug) : [...props.modelValue, slug],
  );
}
</script>
<template>
  <ul role="list" aria-label="Workspaces" class="flex flex-col gap-0.5">
    <li v-for="option in props.options" :key="option.slug">
      <label
        class="flex min-h-9 cursor-pointer items-center gap-2.5 rounded-lg px-2 text-sm hover:bg-muted/60 max-md:min-h-11"
      >
        <CheckboxInput size="sm" :model-value="props.modelValue.includes(option.slug)" @update:model-value="toggle(option.slug)" />
        <span class="min-w-0 flex-1 truncate">{{ option.name }}</span>
        <span v-if="option.isFailing" class="inline-flex shrink-0 items-center gap-1 text-xs font-medium text-destructive"
          ><span class="size-2 rounded-full bg-destructive" aria-hidden="true" />failing</span
        >
        <span class="w-6 shrink-0 text-end text-xs tabular-nums text-muted-foreground">{{ option.count }}</span>
      </label>
    </li>
  </ul>
</template>
