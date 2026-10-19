<script lang="ts">
import type { DeploymentActivity } from "@/domain/deployments";

/** Defines the deployment activity calendar. */
export interface ActivityGridProps {
  /** Applied, finished rollouts per local day over the range, by attention; undefined until the first read, which draws
   * an empty range. The status filter already narrowed them. */
  readonly activity: DeploymentActivity | undefined;
  /** The day expanded below the calendar, `YYYY-MM-DD`. */
  readonly selected: string | null;
  /** A rollout runs now: today's mark pulses. */
  readonly isLive?: boolean;
}
</script>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from "vue";

import { ActivityCalendar, DeploymentFilters, type CalendarCell } from "@/domain/deployments";

import { CellFills } from "./CellFills";
import { HintPlacement } from "./HintPlacement";

/**
 * A GitHub-style calendar of deployments: week columns over weekday rows, month labels above, in 20px day cells (22px
 * on a phone, which scrolls it sideways) that take only the width they need. A day takes the loudest attention of its
 * applied, finished rollouts: green when none needs anything, amber with a half-dot when one is worth a look, red with
 * a cross when one needs action; four steps of fill follow the count (1, 2–3, 4–6, 7+). A deploy that changed nothing
 * never colours a day, and every day names its date and counts. Today and the chosen day differ in
 * more than thickness: today wears a dashed ring in a muted tone, pulsing while a rollout runs (still under reduced
 * motion); the chosen day a solid primary ring set off from the cell; chosen today keeps the solid ring and adds a small
 * today dot. Arrow keys move between days; a day toggles its list. The shared day tooltip is drawn in the top layer and
 * kept inside the viewport, so neither the panel's edge nor the screen's clips it. The key to all of this sits behind the
 * panel's info button (`ActivityGridInfo`), not under the calendar; the measures above carry the range's totals.
 */
defineOptions({ name: "ActivityGrid" });
const props = withDefaults(defineProps<ActivityGridProps>(), { isLive: false });
const emit = defineEmits<{ select: [day: string | null] }>();

const scroller = ref<HTMLElement | null>(null);
const surface = ref<HTMLElement | null>(null);
const today = DeploymentFilters.localDate(new Date());
/** The day that holds the tab stop; arrow keys move it. */
const active = ref<string | null>(null);
/** The day the shared tooltip describes, the cell it points at, and where it sits once measured. */
const hint = ref<{ label: string; anchor: HTMLElement; left: number; top: number; isPlaced: boolean } | null>(null);
const tip = ref<HTMLElement | null>(null);

const calendar = computed(() => {
  const activity = props.activity;
  if (activity) return ActivityCalendar.build(activity.from, activity.to, activity.days);
  const end = new Date();
  const start = new Date(end);
  start.setMonth(start.getMonth() - 6);
  return ActivityCalendar.build(DeploymentFilters.localDate(start), DeploymentFilters.localDate(end), []);
});
const cells = computed(() => calendar.value.weeks.flatMap((week) => week.cells.filter((cell): cell is CalendarCell => cell !== null)));
const first = computed(() => cells.value[0]?.date ?? today);
const last = computed(() => cells.value.at(-1)?.date ?? today);
const tabStop = computed(() => {
  const wanted = active.value ?? props.selected ?? (today <= last.value && today >= first.value ? today : last.value);
  return wanted >= first.value && wanted <= last.value ? wanted : last.value;
});
function fill(cell: CalendarCell): string {
  return CellFills[cell.tone][cell.level] ?? "bg-muted";
}

/**
 * A mark readable on its fill in either scheme. The strong steps take the tone's own foreground — white on the light
 * theme's deep fills, dark on the dark theme's pale ones; the faint steps take the text colour, except the dark theme's
 * second step, already pale enough for the tone's dark foreground.
 */
function markColour(cell: CalendarCell): string {
  const warning = cell.tone === "warning";
  if (cell.level >= 3) return warning ? "text-warning-foreground/90" : "text-destructive-foreground/90";
  if (cell.level === 2)
    return warning ? "text-foreground/65 dark:text-warning-foreground/90" : "text-foreground/65 dark:text-destructive-foreground/90";
  return "text-foreground/65";
}

/** Keeps the newest weeks in view: on a phone the calendar scrolls sideways and opens at today. */
watch(
  () => calendar.value.weeks.length,
  async () => {
    await nextTick();
    if (scroller.value) scroller.value.scrollLeft = scroller.value.scrollWidth;
  },
  { immediate: true, flush: "post" },
);

function show(event: Event, cell: CalendarCell): void {
  const anchor = event.currentTarget as HTMLElement | null;
  if (!anchor) return;
  hint.value = { label: cell.label, anchor, left: 0, top: 0, isPlaced: false };
  void nextTick(place);
}

/** Measures the tooltip and sets it beside its cell, inside the viewport. */
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

function hide(): void {
  hint.value = null;
}

/* While it shows, the tooltip follows its cell through any scroll — the page's or the calendar's own — and a resize. */
watch(
  () => hint.value !== null,
  (shown) => {
    if (shown) {
      window.addEventListener("scroll", place, true);
      window.addEventListener("resize", place);
    } else {
      window.removeEventListener("scroll", place, true);
      window.removeEventListener("resize", place);
    }
  },
);
onBeforeUnmount(() => {
  window.removeEventListener("scroll", place, true);
  window.removeEventListener("resize", place);
});

function choose(cell: CalendarCell): void {
  active.value = cell.date;
  emit("select", props.selected === cell.date ? null : cell.date);
}

async function move(event: KeyboardEvent, cell: CalendarCell): Promise<void> {
  let next: string | null = null;
  if (event.key === "Home") next = first.value;
  else if (event.key === "End") next = last.value;
  else next = ActivityCalendar.step(cell.date, event.key);
  if (!next) return;
  event.preventDefault();
  if (next < first.value || next > last.value) return;
  active.value = next;
  await nextTick();
  const button = surface.value?.querySelector<HTMLButtonElement>(`[data-date="${next}"]`);
  button?.focus();
  button?.scrollIntoView({ block: "nearest", inline: "nearest" });
}
</script>

<template>
  <div ref="surface" class="relative w-max max-w-full min-w-0">
    <div ref="scroller" class="overflow-x-auto overscroll-x-contain pb-1" tabindex="-1">
      <!-- Fixed day cells: 22px with 3px gaps on a phone, for touch; 20px with 4px gaps from md. -->
      <div
        class="grid w-max gap-[3px] p-1.5 [--wh-cell:1.375rem] md:gap-1 md:[--wh-cell:1.25rem]"
        :style="{
          gridTemplateColumns: `1.75rem repeat(${calendar.weeks.length}, var(--wh-cell))`,
          gridTemplateRows: 'auto repeat(7, var(--wh-cell))',
        }"
        role="group"
        :aria-label="`Deployments per day, ${first} to ${last}`"
      >
        <span
          v-for="month in calendar.months"
          :key="`${month.label}-${month.column}`"
          class="row-start-1 overflow-visible text-[0.6875rem] whitespace-nowrap text-muted-foreground"
          :style="{ gridColumn: `${month.column + 2} / span ${Math.max(1, Math.min(3, calendar.weeks.length - month.column))}` }"
          aria-hidden="true"
          >{{ month.label }}</span
        >
        <span
          v-for="(weekday, row) in calendar.weekdays"
          :key="weekday"
          class="sticky left-0 z-[1] col-start-1 flex items-center bg-card pr-1 text-[0.6875rem] text-muted-foreground"
          :style="{ gridRow: row + 2 }"
          aria-hidden="true"
          >{{ row % 2 === 0 ? weekday : "" }}</span
        >
        <template v-for="(week, column) in calendar.weeks" :key="week.start">
          <template v-for="cell in week.cells" :key="cell?.date ?? `${week.start}-gap`">
            <button
              v-if="cell"
              type="button"
              :data-date="cell.date"
              :tabindex="cell.date === tabStop ? 0 : -1"
              :aria-label="cell.label"
              :aria-pressed="props.selected === cell.date"
              :aria-current="cell.date === today ? 'date' : undefined"
              :class="[
                'relative flex size-(--wh-cell) items-center justify-center rounded-[3px] transition-[outline-color] focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-primary',
                fill(cell),
                props.selected === cell.date ? 'ring-2 ring-primary ring-offset-1 ring-offset-card' : '',
              ]"
              :style="{ gridColumn: column + 2, gridRow: cell.row + 2 }"
              @click="choose(cell)"
              @keydown="move($event, cell)"
              @mouseenter="show($event, cell)"
              @mouseleave="hide"
              @focus="show($event, cell)"
              @blur="hide"
            >
              <svg v-if="cell.mark === 'cross'" viewBox="0 0 8 8" :class="['size-2', markColour(cell)]" aria-hidden="true">
                <path d="M1.5 1.5l5 5M6.5 1.5l-5 5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
              </svg>
              <svg v-else-if="cell.mark === 'half'" viewBox="0 0 8 8" :class="['size-2', markColour(cell)]" aria-hidden="true">
                <circle cx="4" cy="4" r="3" fill="none" stroke="currentColor" stroke-width="1.2" />
                <path d="M4 1a3 3 0 0 1 0 6z" fill="currentColor" />
              </svg>
              <template v-if="cell.date === today">
                <span
                  v-if="props.selected === cell.date"
                  :class="['wh-today-dot', props.isLive ? 'wh-live' : '']"
                  aria-hidden="true"
                />
                <span v-else :class="['wh-today-ring', props.isLive ? 'wh-live' : '']" aria-hidden="true" />
              </template>
            </button>
          </template>
        </template>
      </div>
    </div>
    <Teleport to="body">
      <div
        v-if="hint"
        ref="tip"
        role="tooltip"
        class="pointer-events-none fixed z-tooltip max-w-[calc(100vw-1rem)] rounded-md bg-foreground px-2 py-1 text-xs whitespace-nowrap text-background shadow-md"
        :style="{ left: `${hint.left}px`, top: `${hint.top}px`, visibility: hint.isPlaced ? 'visible' : 'hidden' }"
      >
        {{ hint.label }}
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
/* Today: a dashed ring in a muted tone, set off from the cell like the chosen day's but never solid or primary. An
   outline rather than a border: it never widens the scroller, so the newest week can sit at the panel's edge. */
.wh-today-ring {
  position: absolute;
  inset: 0;
  border-radius: 3px;
  outline: 1.5px dashed var(--color-muted-foreground);
  outline-offset: 1.5px;
  pointer-events: none;
}

/* Today when it is also the chosen day: the chosen day's solid ring, and this dot on its corner. */
.wh-today-dot {
  position: absolute;
  top: -4px;
  right: -4px;
  width: 7px;
  height: 7px;
  border-radius: 9999px;
  background: var(--color-foreground);
  box-shadow: 0 0 0 1.5px var(--color-card);
  pointer-events: none;
}

/* While a rollout runs, today's mark pulses; under reduced motion it holds still. */
@media (prefers-reduced-motion: no-preference) {
  .wh-live {
    animation: wh-pulse 1.6s ease-in-out infinite;
  }
}

@keyframes wh-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.25;
  }
}
</style>
