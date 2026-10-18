<script lang="ts">
/** One section of an info panel, and one page of its first-run intro. */
export interface InfoStep {
  /** Names the slot that holds the section's body. */
  readonly key: string;
  /** The section's heading, and the intro page's title. */
  readonly title: string;
  /** A CSS selector for the element the intro page points at; the intro rings it while the page shows. */
  readonly target?: string;
}

/** How an info panel reads: every section at once, or one intro page at a time. */
export const InfoMode = {
  /** Every section, opened from the info button. */
  Reference: "reference",
  /** One section per page, shown once on the first visit. */
  Intro: "intro",
} as const;
export type InfoMode = (typeof InfoMode)[keyof typeof InfoMode];

/** Defines the body an info popover or sheet shows. */
export interface InfoPopoverContentProps {
  /** What the content explains; heads the panel. */
  readonly title: string;
  /** The id the heading carries, which names the surrounding dialog. */
  readonly titleId: string;
  /** The sections, in reading order. */
  readonly steps: readonly InfoStep[];
  /** Every section at once, or one intro page. */
  readonly mode: InfoMode;
  /** The intro page shown, from 0. */
  readonly step: number;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { ArrowLeft, ArrowRight, Info, X } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";

/**
 * The body of an info panel. Opened from its button it lists every section under the panel's title; as a first-run
 * intro it shows one section per page with its place (`2 of 3`), Back, Next and Done, and on the last page says where
 * the content lives from now on. The heading slot lets a bottom sheet put its own title in the heading's place.
 */
defineOptions({ name: "InfoPopoverContent" });
const props = defineProps<InfoPopoverContentProps>();
const emit = defineEmits<{ close: []; step: [index: number] }>();
defineSlots<{
  [key: string]: ((props: { title?: string; id?: string }) => unknown) | undefined;
}>();

const isIntro = computed(() => props.mode === InfoMode.Intro && props.steps.length > 0);
const current = computed(() => props.steps[Math.min(Math.max(props.step, 0), props.steps.length - 1)]);
const isLast = computed(() => props.step >= props.steps.length - 1);
/** One section needs no sub-heading: the panel's title already names it. */
const isSingle = computed(() => props.steps.length === 1);
</script>

<template>
  <div class="flex min-w-0 flex-col gap-3 text-sm" :data-info-content="props.titleId">
    <div class="flex items-start justify-between gap-3">
      <div class="min-w-0">
        <p v-if="isIntro && !isSingle" class="text-xs text-muted-foreground tabular-nums">
          {{ props.title }} · {{ props.step + 1 }} of {{ props.steps.length }}
        </p>
        <slot name="heading" :title="isIntro && current ? current.title : props.title" :id="props.titleId">
          <h2 :id="props.titleId" class="text-base font-semibold">
            {{ isIntro && current ? current.title : props.title }}
          </h2>
        </slot>
      </div>
      <Button
        variant="ghost"
        tone="neutral"
        size="sm"
        shape="square"
        class="-mt-1 -mr-1 shrink-0 max-md:size-11"
        :aria-label="isIntro ? 'Close the introduction' : 'Close'"
        @click="emit('close')"
      >
        <X :size="16" aria-hidden="true" />
      </Button>
    </div>

    <template v-if="isIntro && current">
      <div class="min-w-0 text-foreground/90" aria-live="polite">
        <slot :name="current.key" />
      </div>
      <p v-if="isLast" class="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Info :size="12" class="shrink-0" aria-hidden="true" />Reopen this from the info button.
      </p>
      <div class="flex items-center justify-end gap-2 border-t border-border pt-3">
        <Button
          v-if="props.step > 0"
          variant="ghost"
          tone="neutral"
          size="sm"
          class="max-md:min-h-11"
          @click="emit('step', props.step - 1)"
        >
          <template #leading><ArrowLeft :size="14" aria-hidden="true" /></template>Back
        </Button>
        <Button
          v-if="!isLast"
          variant="solid"
          tone="primary"
          size="sm"
          class="max-md:min-h-11"
          data-info-primary
          @click="emit('step', props.step + 1)"
        >
          Next<template #trailing><ArrowRight :size="14" aria-hidden="true" /></template>
        </Button>
        <Button
          v-else
          variant="solid"
          tone="primary"
          size="sm"
          class="max-md:min-h-11"
          data-info-primary
          @click="emit('close')"
        >
          Done
        </Button>
      </div>
    </template>

    <div v-else class="flex min-w-0 flex-col gap-4">
      <section v-for="item in props.steps" :key="item.key" class="min-w-0">
        <h3 v-if="!isSingle" class="mb-1 text-sm font-semibold">
          {{ item.title }}
        </h3>
        <div class="text-foreground/90"><slot :name="item.key" /></div>
      </section>
    </div>
  </div>
</template>
