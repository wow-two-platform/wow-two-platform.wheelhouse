<script lang="ts">
import type { DayRow, DeploymentRange } from "@/domain/deployments";

/** Defines the chosen day as one compact row per product. */
export interface DayProductRowsProps {
  /** The day's rows (`DayLanes.rows`); undefined until the first read, which draws placeholder rows. */
  readonly rows: readonly DayRow[] | undefined;
  /** The lanes' environments, in column order (`DayLanes.environments`). */
  readonly environments: readonly string[];
  /** The view's range, which the release a glyph opens keeps, so an older day stays in reach. */
  readonly range: DeploymentRange;
}
</script>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from "vue";
import { RouterLink, useRouter } from "vue-router";
import { ArrowUpRight } from "lucide-vue-next";

import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";

import { ConsolePaths, type ConsoleLocation } from "@/application/navigation";
import { releaseLocation } from "@/application/deployments";
import { DayLanes, type DayGlyph } from "@/domain/deployments";
import { deployStateClass, deployStateIcon, statusIconClass } from "@/presentation/common/components";

import { HintPlacement } from "./HintPlacement";

/**
 * The chosen day at a glance: one row per product — its name, then a lane per environment holding one glyph per
 * deploy that day, oldest to newest, so a lane's last glyph is what that environment runs now. A glyph is the state's
 * icon in its attention's colour, dashed when the deploy changed nothing. A thin bar on a row's left edge says its
 * latest deploys need action (red) or a look (amber); those rows lead, then the rest, by name. Pointing at or focusing
 * a glyph shows its release, time, state and actor in a tooltip drawn in the top layer; a click opens the release on
 * the product's Deployments tab. On touch, a tap shows the same tooltip with a link to it. Glyphs wrap inside a narrow
 * lane, and a lane narrows rather than squeeze the name below 7rem. A name wraps to two lines between its words, never
 * inside one: a word longer than the column ends in an ellipsis, the whole name in its title. Causes, services and
 * actions live in the release's rows and the deployment's own page, not here.
 */
defineOptions({ name: "DayProductRows" });
const props = defineProps<DayProductRowsProps>();
defineSlots<{}>();

const router = useRouter();
/** The glyph the shared tooltip describes, the element it points at, where it sits, and whether a tap pinned it. */
const hint = ref<{
  glyph: DayGlyph;
  text: string;
  anchor: HTMLElement;
  left: number;
  top: number;
  isPlaced: boolean;
  isPinned: boolean;
} | null>(null);
const tip = ref<HTMLElement | null>(null);
/** The pointer that pressed last: a touch tap pins the tooltip rather than opening the release. */
let pointer = "";

/**
 * How many glyphs a lane fits on one line before it wraps: the day's busiest lane, at least two (so the lanes keep a
 * steady width) and at most four, three on a phone; the product's name takes the rest of the row.
 */
const slots = computed(() => {
  const busiest = Math.max(0, ...(props.rows ?? []).flatMap((row) => row.lanes.map((lane) => lane.glyphs.length)));
  const wide = Math.min(4, Math.max(2, busiest));
  return {
    "--wh-lane-n": String(wide),
    "--wh-lane-np": String(Math.min(3, wide)),
    "--wh-lanes": String(Math.max(1, props.environments.length)),
  };
});

/** A name's words, which wrap only between one another. */
function wordsOf(name: string): string[] {
  return name.split(/\s+/).filter(Boolean);
}

const BarClass = { act: "bg-destructive", look: "bg-warning" } as const;
const BarWords = { act: "needs action", look: "worth a look" } as const;

/** Where a glyph leads: its release on the product's Deployments tab, or the deployment's page for a removed product. */
function locationOf(glyph: DayGlyph): ConsoleLocation {
  return (
    releaseLocation(glyph.item, props.range) ?? {
      path: `${ConsolePaths.deployments}/${encodeURIComponent(glyph.item.id)}`,
      query: {},
    }
  );
}

function hrefOf(glyph: DayGlyph): string {
  return router.resolve(locationOf(glyph)).href;
}

function labelOf(glyph: DayGlyph): string {
  return `${glyph.item.environment ?? "Unknown environment"}: ${DayLanes.hint(glyph)}. Opens the release.`;
}

function show(event: Event, glyph: DayGlyph, isPinned = false): void {
  const anchor = event.currentTarget as HTMLElement | null;
  if (!anchor) return;
  if (hint.value?.isPinned && !isPinned) return;
  hint.value = { glyph, text: DayLanes.hint(glyph), anchor, left: 0, top: 0, isPlaced: false, isPinned };
  void nextTick(place);
}

function hide(): void {
  if (!hint.value?.isPinned) hint.value = null;
}

function close(): void {
  hint.value = null;
}

/** Measures the tooltip and sets it beside its glyph, inside the viewport. */
function place(): void {
  const current = hint.value;
  const element = tip.value;
  if (!current || !element) return;
  const anchor = current.anchor.getBoundingClientRect();
  const position = HintPlacement.place(
    { left: anchor.left, top: anchor.top, width: anchor.width, height: anchor.height },
    { width: element.offsetWidth, height: element.offsetHeight },
    { width: document.documentElement.clientWidth, height: window.innerHeight },
  );
  hint.value = { ...current, left: position.left, top: position.top, isPlaced: true };
}

/** A click opens the release; a touch tap shows the tooltip with its link instead, and a second tap closes it. */
function activate(event: MouseEvent, glyph: DayGlyph): void {
  const isTouch = pointer === "touch";
  pointer = "";
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  if (isTouch) {
    if (hint.value?.isPinned && hint.value.glyph === glyph) close();
    else show(event, glyph, true);
    return;
  }
  close();
  void router.push(locationOf(glyph));
}

/** A pinned tooltip closes on a press outside it and its glyph, or on Escape. @internal */
function dismiss(event: Event): void {
  if (!hint.value?.isPinned) return;
  if (event instanceof KeyboardEvent) {
    if (event.key === "Escape") close();
    return;
  }
  const target = event.target as Node | null;
  if (target && (tip.value?.contains(target) || hint.value.anchor.contains(target))) return;
  close();
}

/* While it shows, the tooltip follows its glyph through any scroll and a resize; a pinned one listens for dismissal. */
watch(
  () => hint.value !== null,
  (shown) => (shown ? listen() : unlisten()),
);
/* New rows replace the glyph it pointed at. */
watch(
  () => props.rows,
  () => close(),
);
onBeforeUnmount(unlisten);

function listen(): void {
  window.addEventListener("scroll", place, true);
  window.addEventListener("resize", place);
  document.addEventListener("pointerdown", dismiss, true);
  document.addEventListener("keydown", dismiss);
}

function unlisten(): void {
  window.removeEventListener("scroll", place, true);
  window.removeEventListener("resize", place);
  document.removeEventListener("pointerdown", dismiss, true);
  document.removeEventListener("keydown", dismiss);
}
</script>

<template>
  <div class="@container min-w-0">
    <!-- A glyph is 24px (26px on a phone) with 4px gaps; a lane's column fits its slots and its 8px end padding. -->
    <table class="w-full table-fixed border-separate border-spacing-0 text-sm" :style="slots">
      <caption class="sr-only">
        The day's deploys by product and environment, oldest first in each lane
      </caption>
      <colgroup>
        <col />
        <col v-for="environment in props.environments" :key="environment" class="wh-lane" />
      </colgroup>
      <thead>
        <tr class="text-[0.6875rem] font-medium tracking-wide text-muted-foreground uppercase">
          <th scope="col" class="pb-1.5 ps-3 text-left font-medium">Product</th>
          <th v-for="environment in props.environments" :key="environment" scope="col" class="pb-1.5 text-left font-medium">
            {{ environment }}
          </th>
        </tr>
      </thead>
      <tbody v-if="!props.rows">
        <tr v-for="index in 4" :key="index" aria-hidden="true">
          <td class="border-t border-border py-2 ps-3 pe-3"><SkeletonState class="h-4 w-24 rounded" /></td>
          <td v-for="environment in props.environments" :key="environment" class="border-t border-border py-2 pe-2">
            <SkeletonState class="size-6 rounded-md" />
          </td>
        </tr>
      </tbody>
      <tbody v-else>
        <tr v-for="row in props.rows" :key="row.key">
          <th scope="row" class="relative border-t border-border py-1.5 ps-3 pe-3 text-left align-middle font-medium">
            <span
              v-if="row.attention"
              :class="['absolute inset-y-1.5 left-0 w-[3px] rounded-full', BarClass[row.attention]]"
              aria-hidden="true"
            />
            <!-- Each word truncates on its own, so a line breaks only between words. -->
            <span class="line-clamp-2" :title="row.name"
              ><template v-for="(word, index) in wordsOf(row.name)" :key="index"
                >{{ index ? " " : "" }}<span class="inline-block max-w-full truncate align-top">{{ word }}</span></template
              ></span
            >
            <span v-if="row.attention" class="sr-only">, {{ BarWords[row.attention] }}</span>
          </th>
          <td v-for="lane in row.lanes" :key="lane.environment" class="border-t border-border py-1.5 pe-2 align-middle">
            <ul
              v-if="lane.glyphs.length"
              class="flex flex-wrap gap-1"
              :aria-label="`${lane.environment}, ${lane.glyphs.length} ${lane.glyphs.length === 1 ? 'deploy' : 'deploys'}, oldest first`"
            >
              <li v-for="glyph in lane.glyphs" :key="glyph.item.id" class="flex">
                <a
                  :href="hrefOf(glyph)"
                  :aria-label="labelOf(glyph)"
                  :class="[
                    'inline-flex size-6 shrink-0 items-center justify-center rounded-md border transition-colors focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-primary max-md:size-[1.625rem]',
                    deployStateClass(glyph.reading),
                    hint?.isPinned && hint.glyph === glyph ? 'ring-2 ring-ring/50' : '',
                  ]"
                  @pointerdown="pointer = $event.pointerType"
                  @click="activate($event, glyph)"
                  @mouseenter="show($event, glyph)"
                  @mouseleave="hide"
                  @focus="show($event, glyph)"
                  @blur="hide"
                >
                  <component
                    :is="deployStateIcon(glyph.reading.state, glyph.reading.substate)"
                    :size="14"
                    aria-hidden="true"
                    :class="statusIconClass(glyph.reading.state)"
                  />
                </a>
              </li>
            </ul>
            <span v-else class="ps-1.5 text-muted-foreground" :aria-label="`${lane.environment}, no deploys`">—</span>
          </td>
        </tr>
      </tbody>
    </table>
    <Teleport to="body">
      <div
        v-if="hint"
        ref="tip"
        :role="hint.isPinned ? 'dialog' : 'tooltip'"
        :aria-label="hint.isPinned ? hint.text : undefined"
        :class="[
          'fixed z-tooltip flex max-w-[calc(100vw-1rem)] flex-col gap-1 rounded-md bg-foreground px-2 py-1 text-xs text-background shadow-md',
          hint.isPinned ? 'pointer-events-auto py-1.5' : 'pointer-events-none',
        ]"
        :style="{ left: `${hint.left}px`, top: `${hint.top}px`, visibility: hint.isPlaced ? 'visible' : 'hidden' }"
      >
        <span>{{ hint.text }}</span>
        <RouterLink
          v-if="hint.isPinned"
          :to="locationOf(hint.glyph)"
          class="inline-flex min-h-9 items-center gap-1 self-start font-medium underline underline-offset-2"
          @click="close"
          >Open the release<ArrowUpRight :size="12" aria-hidden="true"
        /></RouterLink>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
/* A lane's column fits its slots, but leaves the name at least 7rem: past that it narrows and its glyphs wrap, one glyph
   wide at the least. Container units read the rows' own width, so the side panel and a phone both hold. */
.wh-lane {
  width: max(2rem, min(calc(var(--wh-lane-n) * 1.75rem + 0.25rem), calc((100cqw - 7rem) / var(--wh-lanes))));
}

@media (width < 48rem) {
  .wh-lane {
    width: max(2.125rem, min(calc(var(--wh-lane-np) * 1.875rem + 0.25rem), calc((100cqw - 7rem) / var(--wh-lanes))));
  }
}
</style>
