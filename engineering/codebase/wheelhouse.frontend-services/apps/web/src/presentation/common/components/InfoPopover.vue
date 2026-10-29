<script lang="ts">
import type { InfoSection } from "./InfoPopoverContent.vue";

/** Defines an info button and the explanation it opens. */
export interface InfoPopoverProps {
  /** What the content explains: names the button (`About {title}`) and heads the panel. */
  readonly title: string;
  /** The sections, in reading order; each body is the slot named by its `key`. Without any, the default slot is the body. */
  readonly sections?: readonly InfoSection[];
}
</script>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useId, useTemplateRef } from "vue";
import { Info } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { TailwindBreakpoints, useBreakpointBelow } from "@wow-two-beta/fe/foundation/device";
import {
  BottomSheet,
  BottomSheetTitle,
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@wow-two-beta/fe/presentation/overlays";

import InfoPopoverContent from "./InfoPopoverContent.vue";

/** The region an info button belongs to; the nearest one reveals it. */
const ScopeSelector = "[data-info-scope]";
/** The tallest a popover grows, and the margin it keeps from the screen's edges, in px. @internal */
const PanelMaxHeight = 480;
const ScreenMargin = 16;
/** Below the button, a popover has room enough once this tall; else it takes the taller side. @internal */
const EnoughRoom = 320;

/**
 * An info ("i") button beside a title, holding the context a region needs once: a legend, what a control applies to,
 * the gestures it takes, the period and units of its figures. Pressed, it opens every section — a popover from `md`,
 * a bottom sheet on a phone — and it never opens by itself.
 *
 * The button is dimmed. On a device that hovers with a fine pointer it stays hidden until the pointer is over its
 * region (the nearest `[data-info-scope]` ancestor) or focus is inside it, so it is always reachable by keyboard; on a
 * touch device it is always shown, dimmed, since nothing hovers there. Outside any region it always shows.
 */
defineOptions({ name: "InfoPopover" });
const props = withDefaults(defineProps<InfoPopoverProps>(), { sections: () => [] });
defineSlots<Record<string, (() => unknown) | undefined>>();

const open = ref(false);
const titleId = `info-${useId()}`;
const isPhone = useBreakpointBelow(TailwindBreakpoints, "md");
const sectionKeys = computed(() => props.sections.map((item) => item.key));
const anchor = useTemplateRef<HTMLElement>("anchor");

/* ---- Reveal while the pointer or focus is in the region ---- */

const scope = ref<HTMLElement | null>(null);
const isPointerIn = ref(false);
const isFocusIn = ref(false);
/** Shown at full strength: no region to wait for, the pointer or focus inside it, or the panel open. */
const isRevealed = computed(() => !scope.value || isPointerIn.value || isFocusIn.value || open.value);

function enter(): void {
  isPointerIn.value = true;
}
function leave(): void {
  isPointerIn.value = false;
}
function focusIn(): void {
  isFocusIn.value = true;
}
function focusOut(event: FocusEvent): void {
  if (!scope.value?.contains(event.relatedTarget as Node | null)) isFocusIn.value = false;
}

onMounted(() => {
  const region = anchor.value?.parentElement?.closest<HTMLElement>(ScopeSelector) ?? null;
  if (!region) return;
  scope.value = region;
  isPointerIn.value = region.matches(":hover");
  isFocusIn.value = region.contains(document.activeElement);
  region.addEventListener("pointerenter", enter);
  region.addEventListener("pointerleave", leave);
  region.addEventListener("focusin", focusIn);
  region.addEventListener("focusout", focusOut);
});

onBeforeUnmount(() => {
  const region = scope.value;
  if (!region) return;
  region.removeEventListener("pointerenter", enter);
  region.removeEventListener("pointerleave", leave);
  region.removeEventListener("focusin", focusIn);
  region.removeEventListener("focusout", focusOut);
});

/* ---- Open and close ---- */

/**
 * Where the popover opens and how tall it may grow: below the button when there is room, else on the taller side, never
 * past the screen's edge — it scrolls inside instead. The positioner only flips, so the room is measured here.
 */
const room = ref<{ placement: "bottom-start" | "top-start"; maxHeight: number }>({
  placement: "bottom-start",
  maxHeight: PanelMaxHeight,
});

function measureRoom(): void {
  const rect = anchor.value?.getBoundingClientRect();
  if (!rect) return;
  const below = window.innerHeight - rect.bottom - ScreenMargin * 1.5;
  const above = rect.top - ScreenMargin * 1.5;
  const isBelow = below >= EnoughRoom || below >= above;
  room.value = {
    placement: isBelow ? "bottom-start" : "top-start",
    maxHeight: Math.max(160, Math.min(PanelMaxHeight, isBelow ? below : above)),
  };
}

/** On a phone the button opens the sheet, and keeps the popover's own toggle out of it. @internal */
function press(event: MouseEvent): void {
  if (!isPhone.value) {
    if (!open.value) measureRoom();
    return;
  }
  event.preventDefault();
  open.value = true;
}

function setPopover(isOpen: boolean): void {
  open.value = isOpen;
}

function setSheet(isOpen: boolean): void {
  if (!isOpen) open.value = false;
}

function close(): void {
  open.value = false;
}
</script>

<template>
  <span
    ref="anchor"
    class="wh-info inline-flex shrink-0 align-middle"
    :data-scoped="scope ? '' : undefined"
    :data-revealed="isRevealed ? '' : undefined"
  >
    <Popover :open="open && !isPhone" :placement="room.placement" @update:open="setPopover">
      <PopoverTrigger as-child @click="press">
        <Button
          variant="ghost"
          tone="neutral"
          size="sm"
          shape="square"
          class="wh-info-trigger -my-1.5 shrink-0 max-md:-my-3 max-md:size-11"
          :aria-label="`About ${props.title}`"
          :aria-expanded="open"
        >
          <Info :size="16" aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        :aria-labelledby="titleId"
        class="w-[min(22rem,calc(100vw-2rem))] overflow-y-auto p-4"
        :style="{ maxHeight: `${room.maxHeight}px` }"
      >
        <InfoPopoverContent :title="props.title" :title-id="titleId" :sections="props.sections" @close="close">
          <template v-for="key in sectionKeys" :key="key" #[key]><slot :name="key" /></template>
          <template v-if="!sectionKeys.length" #default><slot /></template>
        </InfoPopoverContent>
      </PopoverContent>
    </Popover>
    <BottomSheet v-if="isPhone" :open="open" :snap-points="['55vh', '92vh']" @update:open="setSheet">
      <InfoPopoverContent :title="props.title" :title-id="titleId" :sections="props.sections" @close="close">
        <template #heading="{ title }">
          <BottomSheetTitle class="text-base font-semibold">{{ title }}</BottomSheetTitle>
        </template>
        <template v-for="key in sectionKeys" :key="key" #[key]><slot :name="key" /></template>
        <template v-if="!sectionKeys.length" #default><slot /></template>
      </InfoPopoverContent>
    </BottomSheet>
  </span>
</template>

<style>
/* The info button reads as secondary: dimmed until pointed at, focused or open. Global, since the reveal rules key on
   the region the button sits in. */
.wh-info-trigger {
  color: var(--color-subtle-foreground);
  transition:
    opacity 120ms ease,
    color 120ms ease;
}

.wh-info-trigger:hover,
.wh-info-trigger:focus-visible,
.wh-info-trigger[aria-expanded="true"] {
  color: var(--color-foreground);
}

/* A device that hovers with a fine pointer hides the button until the pointer or focus is in its region; a touch
   device (no hover) always shows it, dimmed. Focus always reveals it, so the keyboard reaches it. */
@media (hover: hover) and (pointer: fine) {
  .wh-info[data-scoped]:not([data-revealed]) .wh-info-trigger:not(:focus-visible) {
    opacity: 0;
  }
}

@media (prefers-reduced-motion: reduce) {
  .wh-info-trigger {
    transition: none;
  }
}
</style>
