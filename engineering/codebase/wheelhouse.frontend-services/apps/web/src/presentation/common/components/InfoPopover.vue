<script lang="ts">
import type { InfoStep } from "./InfoPopoverContent.vue";

/** Defines an info button and the explanation it opens. */
export interface InfoPopoverProps {
  /** What the content explains: names the button (`About {title}`) and heads the panel. */
  readonly title: string;
  /** The sections, in reading order; each body is the slot named by its `key`. */
  readonly steps: readonly InfoStep[];
  /**
   * The first-run intro's id. When set, the panel opens by itself once — the first time it renders ready — and walks
   * the sections one page at a time; the id is then remembered as seen.
   */
  readonly intro?: string;
  /** Whether what the intro explains has rendered; the intro waits for it. Default `true`. */
  readonly isReady?: boolean;
}
</script>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, shallowRef, useId, useTemplateRef, watch } from "vue";
import { Info } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { TailwindBreakpoints, useBreakpoint } from "@wow-two-beta/fe/foundation/device";
import { useInView } from "@wow-two-beta/fe/foundation/observers";
import {
  BottomSheet,
  BottomSheetTitle,
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@wow-two-beta/fe/presentation/overlays";

import { introMemory } from "@/application/intros";

import InfoPopoverContent, { InfoMode } from "./InfoPopoverContent.vue";

/** The top of the screen a sticky strip may cover; the button counts as met only below it. @internal */
const AnchorBand = "-15% 0px 0px 0px";
/** How much of the first page's target must show before the intro opens. @internal */
const TargetShare = 0.6;
/** How long the intro waits once both are in view, so it never pops with a paint or a passing scroll. @internal */
const IntroDelayMs = 700;
/** How often, and how many times, the intro tries again while the owner is busy in a field or a dialog. @internal */
const IntroRetryMs = 3000;
const IntroAttempts = 5;
/** Focus inside one of these means the owner is busy; the intro waits rather than take focus. @internal */
const BusySelector =
  "input, textarea, select, [contenteditable='true'], [role='dialog'], [role='menu'], [role='listbox']";

/**
 * An info ("i") button beside a title, holding explanation that is read once: a legend, what a control reaches, how
 * to read a chart. Pressed, it opens every section — a popover from `md`, a bottom sheet on a phone. With an `intro`
 * id it also opens once by itself, the first time the owner meets the feature (rendered, its button in view, most of
 * the first page's target on screen), as a short stepped intro in a popover anchored to the button it will live behind,
 * on a phone too, so the page it explains stays in sight: one section per page with its target ringed, Back, Next and
 * Done; Escape, the close button or a click outside end it. The intro is remembered as seen the moment it opens, so
 * it never returns; it waits while focus is in a field, a menu or a dialog, and gives up for the visit after a few tries.
 */
defineOptions({ name: "InfoPopover" });
const props = withDefaults(defineProps<InfoPopoverProps>(), { isReady: true });
defineSlots<Record<string, (() => unknown) | undefined>>();

const open = ref(false);
const mode = ref<InfoMode>(InfoMode.Reference);
const step = ref(0);
const titleId = `info-${useId()}`;
const breakpoint = useBreakpoint(TailwindBreakpoints);
const isPhone = computed(() => breakpoint.value === null || breakpoint.value === "sm");
const stepKeys = computed(() => props.steps.map((item) => item.key));
const anchor = useTemplateRef<HTMLElement>("anchor");
const isPending = computed(() => Boolean(props.intro) && !open.value);
/** The first page's target, looked up once the feature has rendered. */
const introTarget = shallowRef<Element | null>(null);
watch(
  () => [props.isReady, props.steps[0]?.target] as const,
  ([isReady, selector]) => {
    introTarget.value = isReady && selector ? document.querySelector(selector) : null;
  },
  { immediate: true, flush: "post" },
);
const anchorView = useInView(anchor, () => ({
  rootMargin: AnchorBand,
  threshold: 1,
  disabled: !isPending.value,
  fallbackInView: false,
}));
const targetView = useInView(introTarget, () => ({
  threshold: TargetShare,
  disabled: !isPending.value || !introTarget.value,
  fallbackInView: false,
}));
/** The owner meets the feature: its button shows whole, and most of what the first page points at shows too. */
const inView = computed(() => anchorView.inView.value && (!introTarget.value || targetView.inView.value));

/** Every section on a phone opens in a bottom sheet; the intro and everything from `md` open in the popover. */
const isSheet = computed(() => isPhone.value && mode.value === InfoMode.Reference);
/** The intro sits above its button, leaving what it explains in sight below; the sections open beneath it. */
const placement = computed(() => (mode.value === InfoMode.Intro ? "top-start" : "bottom-start"));

/**
 * The button opens every section; pressed while the panel is open, it closes it. On a phone it opens the sheet, and
 * keeps the popover's own toggle out of it. @internal
 */
function press(event: MouseEvent): void {
  if (isPhone.value) {
    event.preventDefault();
    mode.value = InfoMode.Reference;
    open.value = true;
  } else if (!open.value) mode.value = InfoMode.Reference;
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

function goTo(index: number): void {
  step.value = Math.min(Math.max(index, 0), props.steps.length - 1);
  void focusPrimary();
}

/** Puts focus on the intro's Next or Done, once the panel has mounted and its own focus has settled. @internal */
async function focusPrimary(): Promise<void> {
  await nextTick();
  requestAnimationFrame(() =>
    document.querySelector<HTMLElement>(`[data-info-content="${titleId}"] [data-info-primary]`)?.focus(),
  );
}

/* ---- First-run intro ---- */

let timer = 0;
let attempts = 0;

function isBusy(): boolean {
  if (document.activeElement?.closest(BusySelector)) return true;
  return document.querySelector("[role='dialog'][aria-modal='true']") !== null;
}

function startIntro(): void {
  const id = props.intro;
  if (!id || !props.isReady || !inView.value || open.value || introMemory.hasSeen(id) || !props.steps.length) return;
  if (isBusy()) {
    if (++attempts < IntroAttempts) timer = window.setTimeout(startIntro, IntroRetryMs);
    return;
  }
  introMemory.markSeen(id);
  mode.value = InfoMode.Intro;
  step.value = 0;
  open.value = true;
  void focusPrimary();
}

watch(
  () => [props.intro, props.isReady, inView.value] as const,
  ([id, isReady, isInView]) => {
    window.clearTimeout(timer);
    if (id && isReady && isInView && !introMemory.hasSeen(id)) timer = window.setTimeout(startIntro, IntroDelayMs);
  },
  { immediate: true },
);

/* ---- The ring on the intro page's target ---- */

let spot: Element | null = null;

function clearSpot(): void {
  spot?.removeAttribute("data-intro-spot");
  spot = null;
}

watch(
  [open, mode, step],
  () => {
    clearSpot();
    const target = open.value && mode.value === InfoMode.Intro ? props.steps[step.value]?.target : undefined;
    if (!target) return;
    spot = document.querySelector(target);
    spot?.setAttribute("data-intro-spot", "");
  },
  { flush: "post" },
);

onBeforeUnmount(() => {
  window.clearTimeout(timer);
  clearSpot();
});
</script>

<template>
  <span ref="anchor" class="inline-flex shrink-0 align-middle">
    <Popover :open="open && !isSheet" :placement="placement" @update:open="setPopover">
      <PopoverTrigger as-child @click="press">
        <Button
          variant="ghost"
          tone="neutral"
          size="sm"
          shape="square"
          class="-my-1.5 shrink-0 text-muted-foreground max-md:-my-3 max-md:size-11"
          :aria-label="`About ${props.title}`"
        >
          <Info :size="16" aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent :aria-labelledby="titleId" class="w-[min(22rem,calc(100vw-2rem))] p-4">
        <InfoPopoverContent
          :title="props.title"
          :title-id="titleId"
          :steps="props.steps"
          :mode="mode"
          :step="step"
          @close="close"
          @step="goTo"
        >
          <template v-for="key in stepKeys" :key="key" #[key]><slot :name="key" /></template>
        </InfoPopoverContent>
      </PopoverContent>
    </Popover>
    <BottomSheet v-if="isPhone" :open="open && isSheet" :snap-points="['55vh', '92vh']" @update:open="setSheet">
      <InfoPopoverContent
        :title="props.title"
        :title-id="titleId"
        :steps="props.steps"
        :mode="mode"
        :step="step"
        @close="close"
        @step="goTo"
      >
        <template #heading="{ title }">
          <BottomSheetTitle class="text-base font-semibold">{{ title }}</BottomSheetTitle>
        </template>
        <template v-for="key in stepKeys" :key="key" #[key]><slot :name="key" /></template>
      </InfoPopoverContent>
    </BottomSheet>
  </span>
</template>

<style>
/* The intro page's target: a primary ring set off from it, breathing unless motion is reduced. Global, since the
   target belongs to another component. */
[data-intro-spot] {
  outline: 2px solid var(--color-primary);
  outline-offset: 4px;
  border-radius: 6px;
}

@media (prefers-reduced-motion: no-preference) {
  [data-intro-spot] {
    animation: wh-intro-spot 1.8s ease-in-out infinite;
  }
}

@keyframes wh-intro-spot {
  0%,
  100% {
    outline-color: var(--color-primary);
  }
  50% {
    outline-color: color-mix(in oklab, var(--color-primary) 35%, transparent);
  }
}
</style>
