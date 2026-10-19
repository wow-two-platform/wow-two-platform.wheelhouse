<script lang="ts">
/** One section of an info panel. */
export interface InfoSection {
  /** Names the slot that holds the section's body. */
  readonly key: string;
  /** The section's heading; a panel of one section shows none, since the panel's title names it. */
  readonly title: string;
}

/** Defines the body an info popover or sheet shows. */
export interface InfoPopoverContentProps {
  /** What the content explains; heads the panel. */
  readonly title: string;
  /** The id the heading carries, which names the surrounding dialog. */
  readonly titleId: string;
  /** The sections, in reading order; empty shows the default slot alone. */
  readonly sections: readonly InfoSection[];
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { X } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";

/**
 * The body of an info panel: its title and a close button over every section at once. The heading slot lets a bottom
 * sheet put its own title in the heading's place.
 */
defineOptions({ name: "InfoPopoverContent" });
const props = defineProps<InfoPopoverContentProps>();
const emit = defineEmits<{ close: [] }>();
defineSlots<{
  [key: string]: ((props: { title?: string; id?: string }) => unknown) | undefined;
}>();

/** One section needs no sub-heading: the panel's title already names it. */
const isSingle = computed(() => props.sections.length <= 1);
</script>

<template>
  <div class="flex min-w-0 flex-col gap-3 text-sm">
    <div class="flex items-start justify-between gap-3">
      <slot name="heading" :title="props.title" :id="props.titleId">
        <h2 :id="props.titleId" class="min-w-0 text-base font-semibold">{{ props.title }}</h2>
      </slot>
      <Button
        variant="ghost"
        tone="neutral"
        size="sm"
        shape="square"
        class="-mt-1 -mr-1 shrink-0 max-md:size-11"
        aria-label="Close"
        @click="emit('close')"
      >
        <X :size="16" aria-hidden="true" />
      </Button>
    </div>
    <div v-if="!props.sections.length" class="min-w-0 text-foreground/90"><slot /></div>
    <div v-else class="flex min-w-0 flex-col gap-4">
      <section v-for="item in props.sections" :key="item.key" class="min-w-0">
        <h3 v-if="!isSingle" class="mb-1 text-sm font-semibold">{{ item.title }}</h3>
        <div class="text-foreground/90"><slot :name="item.key" /></div>
      </section>
    </div>
  </div>
</template>
